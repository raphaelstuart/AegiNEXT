using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class LayerAnimationSignedTimeTests
{
    [Theory]
    [InlineData(-2, 6)]
    [InlineData(-5, 1)]
    public void FullVisibleContentRangeIncludesNegativeTimes(int offset, int duration)
    {
        var layer = new ProjectLayer { Start = new(10), End = new(10 + duration), AnimationOffset = new(offset) };

        Assert.Equal((new MediaTime(offset), new MediaTime(offset + duration)), LayerAnimationTiming.GetRange(layer));
        Assert.Equal(new MediaTime(offset), LayerAnimationTiming.ClampTime(layer, new(offset - 1)));
        Assert.Equal(new MediaTime(offset + duration), LayerAnimationTiming.ClampTime(layer, new(offset + duration + 1)));
    }

    [Theory]
    [InlineData(KeyframeInterpolation.HOLD)]
    [InlineData(KeyframeInterpolation.LINEAR)]
    [InlineData(KeyframeInterpolation.EASE_IN)]
    [InlineData(KeyframeInterpolation.EASE_OUT)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT)]
    [InlineData(KeyframeInterpolation.POWER)]
    public void LeftExtensionPreservesOriginalPhaseAndMakesTheAddedAreaEditable(KeyframeInterpolation interpolation)
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY,
            [new(new(0), 0.2, interpolation) { Exponent = 2.5 }, new(new(4), 0.8)]);
        var original = new ProjectLayer { Start = new(5), End = new(9), Tracks = [track] };
        var extended = LayerAnimationTiming.Retime(original, new(3), new(9), TimelineEditMode.CROP);

        Assert.Equal(new MediaTime(-2), extended.AnimationOffset);
        Assert.Equal(new MediaTime(-1), LayerAnimationTiming.ClampTime(extended, new(-1)));
        Assert.Same(track, Assert.Single(extended.Tracks));
        for (var sample = 0; sample <= 40; sample++)
        {
            var sceneTime = new MediaTime(50 + sample, 10);
            var before = SceneEvaluator.EvaluateScalarTrack(track, sceneTime - original.Start);
            var after = SceneEvaluator.EvaluateScalarTrack(extended.Tracks[0], sceneTime - extended.Start + extended.AnimationOffset);
            Assert.Equal(before, after, 12);
        }
    }

    [Fact]
    public void SignedKeysAreValidOnlyWithinTheLayerRangeAndRemainInvalidInPresets()
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY, [new(new(-2), 0.2), new(new(-1), 0.8)]);
        var layer = new ProjectLayer { Start = new(3), End = new(4), AnimationOffset = new(-2), Tracks = [track] };
        ProjectValidator.Validate(new() { Layers = [layer] });
        Assert.Equal(0.5, Assert.Single(SceneEvaluator.Evaluate(new ProjectDocument { Layers = [layer] }, new(7, 2))).Opacity, 12);

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer with
        {
            Tracks = [track with { Keyframes = [new(new(-3), 0.2), new(new(-1), 0.8)] }]
        }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Presets = [new(Guid.NewGuid(), "Signed preset", [track])]
        }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer with
        {
            Tracks = [track with { Keyframes = [new(new(-1), 0.2), new(new(-2), 0.8)] }]
        }] }));
    }

    [Theory]
    [InlineData(KeyframeInterpolation.EASE_IN)]
    [InlineData(KeyframeInterpolation.EASE_OUT)]
    [InlineData(KeyframeInterpolation.POWER)]
    public void RepeatedCropAndStretchPreserveSignedCurveSamples(KeyframeInterpolation interpolation)
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY,
            [new(new(-4), 0.1, interpolation) { Exponent = 2.5 }, new(new(2), 0.9)]);
        var original = new ProjectLayer { Start = new(5), End = new(11), AnimationOffset = new(-4), Tracks = [track] };
        var once = LayerAnimationTiming.Retime(original, new(6), new(10), TimelineEditMode.CROP);
        var twice = LayerAnimationTiming.Retime(once, new(7), new(9), TimelineEditMode.CROP);
        var stretched = LayerAnimationTiming.Retime(twice, new(7), new(11), TimelineEditMode.STRETCH);
        ProjectValidator.Validate(new() { Layers = [stretched] });

        for (var sample = 0; sample <= 40; sample++)
        {
            var time = new MediaTime(70 + sample, 10);
            var oldTime = new MediaTime(7) + (time - new MediaTime(7)) / 2;
            var expected = SceneEvaluator.EvaluateScalarTrack(track, oldTime - original.Start + original.AnimationOffset);
            var actual = SceneEvaluator.EvaluateScalarTrack(stretched.Tracks[0], time - stretched.Start + stretched.AnimationOffset);
            Assert.Equal(expected, actual, 10);
        }
    }
}
