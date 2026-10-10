using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleAnimationTests
{
    [Fact]
    public void OrderedOverlappingRangesResolveStylesAndTransformsAtTheSameContentTime()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 3) { Scale = new(2, 3), Rotation = 10 };
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2) { Pivot = SubtitleAnimationPivot.SUBTITLE_ANCHOR };
        var line = new SubtitleLine { Text = "ABCD", AnimationRanges = [first, second] };
        var document = Document(line,
            Track(new(AnimationProperty.FONT_SIZE), 20, 40),
            Track(new(AnimationProperty.FONT_SIZE, TextRangeId: first.Id), 40, 60),
            Track(new(AnimationProperty.FONT_SIZE, TextRangeId: second.Id), 80, 100),
            Track(new(AnimationProperty.SCALE, TextRangeId: first.Id), new ScenePoint(2, 3), new ScenePoint(4, 5)),
            Track(new(AnimationProperty.ROTATION, TextRangeId: first.Id), 10, 30));

        var evaluated = Assert.Single(SceneEvaluator.Evaluate(document, new(1)));
        var ordinary = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, line.Style, 0, SubtitleAnimationState.NORMAL);
        var overlap = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, line.Style, 1, SubtitleAnimationState.NORMAL);
        var outside = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, line.Style, 3, SubtitleAnimationState.NORMAL);

        Assert.Equal(50, ordinary.FontSize);
        Assert.Equal(90, overlap.FontSize);
        Assert.Equal(30, outside.FontSize);
        Assert.Equal(first.Id, evaluated.AnimationRanges[0].Id);
        Assert.Equal(new ScenePoint(3, 4), evaluated.AnimationRanges[0].Scale);
        Assert.Equal(20, evaluated.AnimationRanges[0].Rotation);
        Assert.Equal(SubtitleAnimationPivot.SUBTITLE_ANCHOR, evaluated.AnimationRanges[1].Pivot);
        Assert.Equal(5, evaluated.AnimationValues.Count);
    }

    [Fact]
    public void ExplicitVisualStatesDoNotChangeSharedLayoutOrOrdinaryPaint()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range] };
        var document = Document(line,
            Track(new(AnimationProperty.FILL, TextRangeId: range.Id), new SceneColor(1, 0, 0), new SceneColor(0, 0, 1)),
            Track(new(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE), SceneColor.Black, SceneColor.White),
            Track(new(AnimationProperty.SHADOW_OFFSET, TextRangeId: range.Id, State: SubtitleAnimationState.INACTIVE), new ScenePoint(-2, 1), new ScenePoint(4, 7)));
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(document, new(1)));
        var normal = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, line.Style, 0, SubtitleAnimationState.NORMAL);
        var active = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, normal, 0, SubtitleAnimationState.ACTIVE);
        var inactive = SubtitleAnimationEvaluation.ApplyStyleAnimations(evaluated, normal, 0, SubtitleAnimationState.INACTIVE);

        Assert.Equal(new SceneColor(0.5, 0, 0.5), normal.Fill);
        Assert.Equal(new SceneColor(0.5, 0.5, 0.5), active.Fill);
        Assert.Equal(normal.Fill, inactive.Fill);
        Assert.Equal(new ScenePoint(1, 4), inactive.ShadowOffset);
        Assert.Equal(normal.FontSize, active.FontSize);
        Assert.Equal(normal.LetterSpacing, inactive.LetterSpacing);
    }

    [Fact]
    public void SrgbInterpolationReturnsLinearSamplesAndKeepsAlphaIndependent()
    {
        var track = Track(new(AnimationProperty.FILL), new SceneColor(0, 0, 0, 0), new SceneColor(1, 1, 1, 1)) with
        {
            ColorSpace = AnimationColorSpace.SRGB,
            Keyframes =
            [
                new(new(0), new SceneColor(0, 0, 0, 0))
                {
                    ComponentCurves = [null, null, new(KeyframeInterpolation.EASE_IN)]
                },
                new(new(2), SceneColor.White)
            ]
        };
        var sample = SceneEvaluator.EvaluateColorTrack(track, new(1));

        Assert.Equal(0.21404114048223255, sample.Red, 12);
        Assert.Equal(sample.Red, sample.Green);
        Assert.Equal(sample.Red, sample.Blue);
        Assert.Equal(0.25, sample.Alpha, 12);
        Assert.Equal(new SceneColor(0, 0, 0, 0), SceneEvaluator.EvaluateColorTrack(track, new(-1)));
        Assert.Equal(SceneColor.White, SceneEvaluator.EvaluateColorTrack(track, new(3)));
    }

    [Fact]
    public void ComponentMasksAndOrderedAlphaMultiplicationPreserveUntouchedChannels()
    {
        var track = new AnimationTrack(AnimationProperty.FILL, [])
        {
            ColorSpace = AnimationColorSpace.SRGB,
            InitialValue = new SceneColor(0, 0.2, 0.3, 0.8),
            Transforms =
            [
                new(Guid.NewGuid(), new(0), new(2), new SceneColor(1, 1, 1, 1)) { ComponentMask = 1 },
                new(Guid.NewGuid(), new(0), new(2), new SceneColor(1, 1, 1, 0.5))
                {
                    ComponentMask = 8, Mode = AnimationTransformMode.MULTIPLY_BY
                }
            ]
        };
        var sample = SceneEvaluator.EvaluateColorTrack(track, new(1));

        Assert.Equal(0.21404114048223255, sample.Red, 12);
        Assert.Equal(0.2, sample.Green);
        Assert.Equal(0.3, sample.Blue);
        Assert.Equal(0.6, sample.Alpha, 12);
    }

    [Fact]
    public void SrgbCroppingAndStretchingPreserveOriginalSamplesAndTargetIdentity()
    {
        var line = new SubtitleLine { Text = "AB", End = new(8) };
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        line = line with { AnimationRanges = [range] };
        var track = Track(new(AnimationProperty.SHADOW_COLOR, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE),
            new SceneColor(0, 2, -0.1, 0.2), new SceneColor(4, 0, 0.7, 0.8)) with
        {
            ColorSpace = AnimationColorSpace.SRGB,
            Keyframes =
            [
                new(new(0), new SceneColor(0, 2, -0.1, 0.2), KeyframeInterpolation.EASE_IN)
                {
                    ComponentCurves = [new(KeyframeInterpolation.EASE_OUT), null, new(KeyframeInterpolation.POWER) { Exponent = 3 }]
                },
                new(new(8), new SceneColor(4, 0, 0.7, 0.8))
            ]
        };
        var original = Document(line, track).Layers[0];
        var cropped = LayerAnimationTiming.Clip(original with { Start = new(1), End = new(7), AnimationOffset = new(1) });
        var stretched = LayerAnimationTiming.Retime(cropped, new(1), new(13), TimelineEditMode.STRETCH);

        Assert.Equal(track.Target, Assert.Single(cropped.Tracks).Target);
        Assert.Equal(AnimationColorSpace.SRGB, Assert.Single(stretched.Tracks).ColorSpace);
        for (var index = 0; index <= 60; index++)
        {
            var time = new MediaTime(10 + index, 10);
            var expected = SceneEvaluator.EvaluateColorTrack(track, time);
            var actual = SceneEvaluator.EvaluateColorTrack(cropped.Tracks[0], time);
            var scaled = SceneEvaluator.EvaluateColorTrack(stretched.Tracks[0], time * 2);
            for (var component = 0; component < 4; component++)
            {
                Assert.Equal(((AnimationValue)expected).GetComponent(component), ((AnimationValue)actual).GetComponent(component), 10);
                Assert.Equal(((AnimationValue)expected).GetComponent(component), ((AnimationValue)scaled).GetComponent(component), 10);
            }
        }
    }

    [Theory]
    [InlineData(AnimationProperty.FONT_SIZE, SubtitleAnimationState.ACTIVE)]
    [InlineData(AnimationProperty.LETTER_SPACING, SubtitleAnimationState.INACTIVE)]
    [InlineData(AnimationProperty.SCALE, SubtitleAnimationState.ACTIVE)]
    [InlineData(AnimationProperty.ROTATION, SubtitleAnimationState.INACTIVE)]
    [InlineData(AnimationProperty.POSITION, SubtitleAnimationState.NORMAL)]
    [InlineData(AnimationProperty.BLUR, SubtitleAnimationState.NORMAL)]
    [InlineData(AnimationProperty.PATH_PROGRESS, SubtitleAnimationState.NORMAL)]
    public void RangeTargetsRejectUnsupportedGeometryAndCompositingProperties(AnimationProperty property, SubtitleAnimationState state)
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range] };
        var value = AnimationPropertyMetadata.GetValueKind(property) == AnimationValueKind.VECTOR
            ? AnimationValue.FromVector(new(1, 1)) : AnimationValue.FromScalar(1);
        var document = Document(line, Track(new(property, TextRangeId: range.Id, State: state), value, value));

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
    }

    [Fact]
    public void RangeIdentityAndGraphemeBoundariesAreValidatedWithoutDisallowingOverlap()
    {
        var first = new SubtitleAnimationRange(Guid.NewGuid(), 0, 3);
        var second = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        var line = new SubtitleLine { Text = "Ae\u0301B", AnimationRanges = [first, second] };
        ProjectValidator.Validate(Document(line));
        foreach (var ranges in new ImmutableArray<SubtitleAnimationRange>[]
        {
            default, [first, first], [first with { Id = Guid.Empty }], [first with { Utf16Start = 2, Utf16Length = 1 }],
            [first with { Utf16Length = 0 }], [first with { Utf16Start = int.MaxValue }],
            [first with { Scale = new(double.NaN, 1) }], [first with { Pivot = (SubtitleAnimationPivot)100 }],
            [.. Enumerable.Range(0, 257).Select(_ => first with { Id = Guid.NewGuid() })]
        })
        {
            Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { AnimationRanges = ranges })));
        }
    }

    [Fact]
    public void TargetsRejectForeignRangesNodeRangeMixturesAndPersistedRangePresets()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "AB", AnimationRanges = [range] };
        foreach (var target in new AnimationTrackTarget[]
        {
            new(AnimationProperty.FILL, TextRangeId: Guid.NewGuid()),
            new(AnimationProperty.FILL, TextRangeId: Guid.Empty),
            new(AnimationProperty.FILL, Guid.NewGuid(), range.Id),
            new(AnimationProperty.FILL, TextRangeId: range.Id, State: (SubtitleAnimationState)100)
        })
        {
            Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line, Track(target, SceneColor.White, SceneColor.Black))));
        }
        var document = Document(line) with
        {
            Presets = [new(Guid.NewGuid(), "Range", [Track(new(AnimationProperty.FILL, TextRangeId: range.Id), SceneColor.White, SceneColor.Black)])]
        };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
    }

    [Fact]
    public void AnimationRangeIdentitiesCannotRepeatAcrossDifferentSubtitles()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var first = new SubtitleLine { Text = "A", AnimationRanges = [range] };
        var second = new SubtitleLine { Text = "B", AnimationRanges = [range] };
        var document = Document(first) with
        {
            Subtitles = [first, second],
            Layers =
            [
                new() { SubtitleId = first.Id, Start = first.Start, End = first.End },
                new() { SubtitleId = second.Id, Start = second.Start, End = second.End }
            ]
        };

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(document));
    }

    [Fact]
    public void TextTrackBudgetIsIndependentOfOrdinaryLayerTracks()
    {
        var ranges = Enumerable.Range(0, 80).Select(_ => new SubtitleAnimationRange(Guid.NewGuid(), 0, 1)).ToImmutableArray();
        var line = new SubtitleLine { Text = "A", AnimationRanges = ranges };
        var tracks = ranges.Select(range => Track(new(AnimationProperty.FONT_SIZE, TextRangeId: range.Id), 20, 40)).ToArray();

        ProjectValidator.Validate(Document(line, tracks));
        Assert.Equal(80, tracks.Length);
    }

    [Fact]
    public void InvalidOperationMasksModesAndNonColorSrgbTracksAreRejected()
    {
        var line = new SubtitleLine { Text = "A" };
        foreach (var mask in new[] { -1, 16, 31 })
        {
            var track = new AnimationTrack(AnimationProperty.FILL, [])
            {
                InitialValue = SceneColor.White,
                Transforms = [new(Guid.NewGuid(), new(0), new(1), SceneColor.Black) { ComponentMask = mask }]
            };
            Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line, track)));
        }
        var invalidMode = new AnimationTrack(AnimationProperty.FILL, [])
        {
            InitialValue = SceneColor.White,
            Transforms = [new(Guid.NewGuid(), new(0), new(1), SceneColor.Black) { Mode = (AnimationTransformMode)100 }]
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line, invalidMode)));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line,
            Track(new(AnimationProperty.FONT_SIZE), 10, 20) with { ColorSpace = AnimationColorSpace.SRGB })));
    }

    [Fact]
    public void LegacyJsonUsesNeutralDefaultsAndNewIdentitiesRoundTrip()
    {
        var oldTarget = JsonSerializer.Deserialize<AnimationTrackTarget>("{\"Property\":19}");
        var oldOperation = JsonSerializer.Deserialize<AnimationTransformOperation>("{\"Id\":\"4ed8c897-03b6-43fa-ac1c-a650dab2acb8\",\"Start\":{},\"End\":{},\"Value\":1}")!;
        Assert.Null(oldTarget.TextRangeId);
        Assert.Equal(SubtitleAnimationState.NORMAL, oldTarget.State);
        Assert.Equal(0, oldOperation.ComponentMask);
        Assert.Equal(AnimationTransformMode.INTERPOLATE_TO, oldOperation.Mode);
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var target = new AnimationTrackTarget(AnimationProperty.SHADOW_COLOR, TextRangeId: range.Id, State: SubtitleAnimationState.ACTIVE);
        var track = Track(target, SceneColor.Black, SceneColor.White) with { ColorSpace = AnimationColorSpace.SRGB };

        Assert.Equal(target, JsonSerializer.Deserialize<AnimationTrackTarget>(JsonSerializer.Serialize(target)));
        Assert.Equal(track.ColorSpace, JsonSerializer.Deserialize<AnimationTrack>(JsonSerializer.Serialize(track))!.ColorSpace);
        Assert.Equal(32, (int)AnimationProperty.FONT_SIZE);
        Assert.Equal(35, (int)AnimationProperty.SHADOW_COLOR);
    }

    [Fact]
    public void FontSizeMultiplicationRejectsInteriorOvershootEvenWhenEveryOperationEndpointFits()
    {
        var line = new SubtitleLine { Text = "A" };
        var track = new AnimationTrack(AnimationProperty.FONT_SIZE, [])
        {
            InitialValue = 3800,
            Transforms =
            [
                new(Guid.NewGuid(), new(0), new(1), 2) { Mode = AnimationTransformMode.MULTIPLY_BY },
                new(Guid.NewGuid(), new(0), new(1), 0.5) { Mode = AnimationTransformMode.MULTIPLY_BY }
            ]
        };
        Assert.Equal(3800, SceneEvaluator.EvaluateScalarTrack(track, new(0)));
        Assert.Equal(3800, SceneEvaluator.EvaluateScalarTrack(track, new(1)));
        Assert.Equal(4275, SceneEvaluator.EvaluateScalarTrack(track, new(1, 2)));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line, track)));
    }

    [Theory]
    [InlineData(1.5, 75)]
    [InlineData(0.5, 45)]
    public void PositiveFontSizeFactorsInterpolateWithoutTreatingTheFactorAsAnAbsoluteSize(double factor, double expected)
    {
        var line = new SubtitleLine { Text = "A" };
        var track = new AnimationTrack(AnimationProperty.FONT_SIZE, [])
        {
            InitialValue = 60,
            Transforms = [new(Guid.NewGuid(), new(0), new(2), factor) { Mode = AnimationTransformMode.MULTIPLY_BY }]
        };
        ProjectValidator.Validate(Document(line, track));
        Assert.Equal(expected, SceneEvaluator.EvaluateScalarTrack(track, new(1)));
    }

    [Fact]
    public void StaticRangeEditingPreservesOtherStylesAndNormalizesOnlyTheSelectedProperty()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        var line = new SubtitleLine
        {
            Text = "ABCD", AnimationRanges = [range],
            InlineSpans = [new(0, 2, new() { Fill = new(1, 0, 0), FontSize = 30 }), new(2, 2, new() { Fill = new(0, 0, 1), FontSize = 50 })]
        };
        var document = Document(line);
        var target = new AnimationTrackTarget(AnimationProperty.FONT_SIZE, TextRangeId: range.Id);
        Assert.False(SubtitleAnimationEvaluation.IsBaseValueUniform(document.Layers[0], line, target));

        var changed = SubtitleAnimationEditing.SetBaseValue(document, document.Layers[0].Id, target, 42);
        ProjectValidator.Validate(changed);
        var updated = changed.Subtitles[0];
        Assert.True(SubtitleAnimationEvaluation.IsBaseValueUniform(changed.Layers[0], updated, target));
        Assert.Equal(42, SubtitleAnimationEvaluation.GetBaseValue(changed.Layers[0], updated, target).Scalar);
        Assert.Equal(4, updated.InlineSpans.Length);
        Assert.Equal(30, updated.InlineSpans[0].Style.FontSize);
        Assert.Equal(new SceneColor(1, 0, 0), updated.InlineSpans[1].Style.Fill);
        Assert.Equal(new SceneColor(0, 0, 1), updated.InlineSpans[2].Style.Fill);
        Assert.Equal(50, updated.InlineSpans[3].Style.FontSize);
        Assert.Equal(range, Assert.Single(updated.AnimationRanges));
        Assert.Same(changed, SubtitleAnimationEditing.SetBaseValue(changed, changed.Layers[0].Id, target, 42));
    }

    [Fact]
    public void StaticStateEditingPreservesTheOtherStateAndRangeTransforms()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        var line = new SubtitleLine
        {
            Text = "ABCD", AnimationRanges = [range],
            KaraokeStyleSpans = [new(0, 4, new() { Fill = new(1, 0, 0) }, new() { ShadowBlur = 4 })]
        };
        var document = Document(line);
        var target = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id, State: SubtitleAnimationState.INACTIVE);
        var changed = SubtitleAnimationEditing.SetBaseValue(document, document.Layers[0].Id, target, new SceneColor(0, 1, 0));
        changed = SubtitleAnimationEditing.SetBaseValue(changed, changed.Layers[0].Id,
            new(AnimationProperty.SCALE, TextRangeId: range.Id), new ScenePoint(2, -1));
        ProjectValidator.Validate(changed);
        var updated = changed.Subtitles[0];

        Assert.Equal(3, updated.KaraokeStyleSpans.Length);
        Assert.All(updated.KaraokeStyleSpans, span => Assert.Equal(new SceneColor(1, 0, 0), span.ActiveStyle!.Fill));
        Assert.Equal(4, updated.KaraokeStyleSpans[1].InactiveStyle!.ShadowBlur);
        Assert.Equal(new SceneColor(0, 1, 0), SubtitleAnimationEvaluation.GetBaseValue(changed.Layers[0], updated, target).Color);
        Assert.Equal(new ScenePoint(2, -1), Assert.Single(updated.AnimationRanges).Scale);
        Assert.Empty(updated.InlineSpans);
    }

    [Fact]
    public void InactiveOutlineStepBaseValueRemainsEditableBeforeTheRenderTimeOutlineHiding()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 1);
        var line = new SubtitleLine
        {
            Text = "A", AnimationRanges = [range], Style = new() { StrokeWidth = 4 },
            Karaoke = [new(0, 1, new(0), new(1), SceneColor.White) { HighlightKind = KaraokeHighlightKind.OUTLINE_STEP }]
        };
        var document = Document(line);
        var target = new AnimationTrackTarget(AnimationProperty.STROKE_WIDTH, TextRangeId: range.Id,
            State: SubtitleAnimationState.INACTIVE);

        Assert.Equal(4, SubtitleAnimationEvaluation.GetBaseValue(document.Layers[0], line, target).Scalar);
        var changed = SubtitleAnimationEditing.SetBaseValue(document, document.Layers[0].Id, target, 6);
        Assert.Equal(6, SubtitleAnimationEvaluation.GetBaseValue(changed.Layers[0], changed.Subtitles[0], target).Scalar);
    }

    [Fact]
    public void TimelineRowsKeepRangeAndStateIdentityIndependentAndAcceptLegacyDefaults()
    {
        var owner = Guid.NewGuid();
        var first = new TimelineAnimationRowId(TimelineRowScope.TRACK, owner, AnimationProperty.FILL);
        var second = first with { TextRangeId = Guid.NewGuid() };
        var third = second with { State = SubtitleAnimationState.ACTIVE };
        new TimelineViewState { CollapsedAnimationRows = [first, second, third] }.Validate();
        Assert.Throws<InvalidDataException>(() => new TimelineViewState { CollapsedAnimationRows = [first, first] }.Validate());
        Assert.Throws<InvalidDataException>(() => new TimelineViewState
        {
            CollapsedAnimationRows = [third with { Property = AnimationProperty.FONT_SIZE }]
        }.Validate());
    }

    private static AnimationTrack Track(AnimationTrackTarget target, AnimationValue start, AnimationValue end)
    {
        return new(target, [new(new(0), start), new(new(2), end)]);
    }

    private static ProjectDocument Document(SubtitleLine line, params AnimationTrack[] tracks)
    {
        return new()
        {
            Subtitles = [line],
            Layers = [new() { SubtitleId = line.Id, Start = line.Start, End = line.End, Tracks = [.. tracks] }]
        };
    }
}
