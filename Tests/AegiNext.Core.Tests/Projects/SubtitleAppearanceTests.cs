using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleAppearanceTests
{
    [Fact]
    public void DefaultsPreserveExistingLayoutAndInlineZeroOverridesRemainExplicit()
    {
        var defaults = new SubtitleStyle();
        Assert.Equal(0, defaults.LetterSpacing);
        Assert.Equal(0, defaults.FillBlur);
        Assert.Equal(0, defaults.StrokeBlur);
        Assert.Equal(SubtitleWrapMode.GRAPHEME, defaults.WrapMode);
        var style = defaults with { LetterSpacing = -3, FillBlur = 4, StrokeBlur = 5, WrapMode = SubtitleWrapMode.NATURAL };
        var first = new SubtitleInlineStyleOverride { LetterSpacing = 0, FillBlur = 0 };
        var combined = first.Merge(new() { StrokeBlur = 0 });

        Assert.True(combined.HasOverrides);
        Assert.Equal(style with { LetterSpacing = 0, FillBlur = 0, StrokeBlur = 0 }, combined.ApplyTo(style));
        Assert.Equal(style, SubtitleInlineStyleOverride.FromStyle(style).ApplyTo(defaults with { WrapMode = style.WrapMode }));
    }

    [Fact]
    public void KaraokeBlurSnapshotsAndEditsPreserveLayoutAndIndependentInheritance()
    {
        var ordinary = new SubtitleStyle { LetterSpacing = -2, FillBlur = 2, StrokeBlur = 3, WrapMode = SubtitleWrapMode.NO_WRAP };
        var snapshot = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Glow", ordinary with { FillBlur = 9, StrokeBlur = 11 });
        var segment = new KaraokeSegment(0, 1, new(0), new(1), SceneColor.White);
        var inactive = KaraokeVisualStyleResolver.ResolveInactive(ordinary, segment, new() { FillBlur = 0 });
        var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, snapshot, segment, new() { StrokeBlur = 0 });

        Assert.Equal(0, inactive.FillBlur);
        Assert.Equal(3, inactive.StrokeBlur);
        Assert.Equal(9, active.FillBlur);
        Assert.Equal(0, active.StrokeBlur);
        Assert.Equal(ordinary.LetterSpacing, active.LetterSpacing);
        Assert.Equal(ordinary.WrapMode, active.WrapMode);
        Assert.False(snapshot.VisuallyEquals(snapshot with { FillBlur = 8 }));
        Assert.False(snapshot.VisuallyEquals(snapshot with { StrokeBlur = 8 }));
        var edit = new KaraokeVisualStyleEdit { FillBlur = 0 };
        Assert.True(edit.HasChanges);
        var overlay = edit.Merge(new() { StrokeBlur = 0 }).ToOverride(ordinary);
        Assert.True(overlay.HasOverrides);
        Assert.Equal(0, overlay.ApplyTo(ordinary).FillBlur);
        Assert.Equal(0, overlay.ApplyTo(ordinary).StrokeBlur);
        Assert.Equal(ordinary, KaraokeVisualStyleEdit.FromStyle(ordinary).ToOverride(new()).ApplyTo(ordinary));
        Assert.Equal(overlay, new KaraokeVisualStyleOverride { FillBlur = 0 }.Merge(new() { StrokeBlur = 0 }));
    }

    [Theory]
    [InlineData(-4096.01)]
    [InlineData(4096.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void LetterSpacingUsesAFiniteSignedPixelRange(double value)
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new() { LetterSpacing = value }));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(512.01)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void EachBlurUsesTheExistingFiniteBlurBudget(double value)
    {
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new() { FillBlur = value }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new() { StrokeBlur = value }));
        var line = new SubtitleLine { Text = "a", KaraokeStyle = new() { PresetId = Guid.NewGuid(), PresetName = "Glow", FillBlur = value } };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleKaraoke(line));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleKaraoke(line with
        {
            KaraokeStyle = line.KaraokeStyle with { FillBlur = 0, StrokeBlur = value }
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleKaraoke(new()
        {
            Text = "a", KaraokeStyleSpans = [new(0, 1, new() { FillBlur = value })]
        }));
    }

    [Fact]
    public void BoundariesAndDefinedWrapModesAreValidAndUnknownModesAreRejected()
    {
        foreach (var mode in Enum.GetValues<SubtitleWrapMode>())
        {
            ProjectValidator.ValidateSubtitleStyle(new() { LetterSpacing = -4096, FillBlur = 0, StrokeBlur = 512, WrapMode = mode });
            ProjectValidator.ValidateSubtitleStyle(new() { LetterSpacing = 4096, FillBlur = 512, StrokeBlur = 0, WrapMode = mode });
        }
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleStyle(new() { WrapMode = (SubtitleWrapMode)99 }));
    }

    [Theory]
    [InlineData(AnimationProperty.LETTER_SPACING, 29, -4096, 4096)]
    [InlineData(AnimationProperty.FILL_BLUR, 30, 0, 512)]
    [InlineData(AnimationProperty.STROKE_BLUR, 31, 0, 512)]
    public void NewScalarTracksHaveStableIdentifiersAndRejectNonSubtitleTargets(AnimationProperty property, int identity,
        double minimum, double maximum)
    {
        Assert.Equal(identity, (int)property);
        Assert.True(AnimationPropertyMetadata.IsSubtitleOnlyProperty(property));
        Assert.False(AnimationPropertyMetadata.IsMaskProperty(property));
        Assert.Contains(property, AnimationPropertyMetadata.CurrentProperties);
        Assert.Equal(AnimationValueKind.SCALAR, AnimationPropertyMetadata.GetValueKind(property));
        Assert.Equal(minimum, AnimationPropertyMetadata.GetMinimum(property));
        Assert.Equal(maximum, AnimationPropertyMetadata.GetMaximum(property));
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1),
            Tracks = [new(property, [new(new(0), 0)])]
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer] }));
    }

    [Fact]
    public void EvaluatedSubtitleValuesUseContentTimeAndDistinguishAnimatedZeroFromTheStyle()
    {
        var line = new SubtitleLine { Text = "a", Start = new(10), End = new(12), Style = new() { LetterSpacing = 3, FillBlur = 4, StrokeBlur = 5 } };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, Start = line.Start, End = line.End, AnimationOffset = new(1, 2),
            Tracks =
            [
                new(AnimationProperty.LETTER_SPACING, [new(new(1, 2), -4), new(new(5, 2), 4)]),
                new(AnimationProperty.FILL_BLUR, [new(new(1, 2), 0)]),
                new(AnimationProperty.STROKE_BLUR, [])
                {
                    InitialValue = 8, Transforms = [new(Guid.NewGuid(), new(1, 2), new(5, 2), 0)]
                }
            ]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        ProjectValidator.Validate(document);
        var value = Assert.Single(SceneEvaluator.Evaluate(document, new(11)));

        Assert.Equal(0, value.LetterSpacing);
        Assert.Equal(0, value.FillBlur);
        Assert.Equal(4, value.StrokeBlur);
        Assert.True(value.HasLetterSpacingAnimation);
        Assert.True(value.HasFillBlurAnimation);
        Assert.True(value.HasStrokeBlurAnimation);
        value = Assert.Single(SceneEvaluator.Evaluate(document with { Layers = [layer with { Tracks = [] }] }, new(11)));
        Assert.Equal(3, value.LetterSpacing);
        Assert.Equal(4, value.FillBlur);
        Assert.Equal(5, value.StrokeBlur);
        Assert.False(value.HasLetterSpacingAnimation);
        Assert.False(value.HasFillBlurAnimation);
        Assert.False(value.HasStrokeBlurAnimation);
    }

    [Fact]
    public void SingleLayerEvaluationUsesContentTimeWithoutVisibilityFilteringOrASecondOffset()
    {
        var line = new SubtitleLine { Text = "a", Start = new(10), End = new(12) };
        var layer = new ProjectLayer
        {
            SubtitleId = line.Id, Start = line.Start, End = line.End, AnimationOffset = new(1, 2),
            Tracks = [new(AnimationProperty.LETTER_SPACING, [new(new(1, 2), 2), new(new(5, 2), 10)])]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var middle = SceneEvaluator.EvaluateLayer(layer, line, new(3, 2));
        Assert.Equal(Assert.Single(SceneEvaluator.Evaluate(document, new(11))), middle);
        Assert.Equal(6, middle.LetterSpacing);
        Assert.Equal(new(3, 2), middle.LocalTime);
        Assert.Empty(SceneEvaluator.Evaluate(document, line.End));
        Assert.Equal(10, SceneEvaluator.EvaluateLayer(layer, line, new(5, 2)).LetterSpacing);
        Assert.Equal(2, SceneEvaluator.EvaluateLayer(layer, line, new(-1)).LetterSpacing);
    }
}
