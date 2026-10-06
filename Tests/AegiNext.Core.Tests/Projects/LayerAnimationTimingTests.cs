using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class LayerAnimationTimingTests
{
    [Theory]
    [InlineData(KeyframeInterpolation.HOLD)]
    [InlineData(KeyframeInterpolation.LINEAR)]
    [InlineData(KeyframeInterpolation.EASE_IN)]
    [InlineData(KeyframeInterpolation.EASE_OUT)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT)]
    public void RepeatedCroppingPreservesEverySampleOfTheOriginalCurve(KeyframeInterpolation interpolation)
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION, [new(new(0), 12, interpolation), new(new(8), 212)]);
        var layer = new ProjectLayer { Start = new(1), End = new(7), AnimationOffset = new(1), Tracks = [track] };
        var once = LayerAnimationTiming.Clip(layer);
        var twice = LayerAnimationTiming.Clip(once with { Start = new(2), End = new(6), AnimationOffset = new(2) });
        var clipped = Assert.Single(twice.Tracks);
        Assert.Equal(new MediaTime(2), clipped.Keyframes[0].Time);
        Assert.Equal(new MediaTime(6), clipped.Keyframes[^1].Time);
        for (var sample = 0; sample <= 80; sample++)
        {
            var time = new MediaTime(40 + sample, 20);
            Assert.True(Math.Abs(SceneEvaluator.EvaluateScalarTrack(track, time) - SceneEvaluator.EvaluateScalarTrack(clipped, time)) <= 1e-9,
                "Cropping must preserve each original curve sample within an absolute error of 1e-9.");
        }

        Assert.Same(twice, LayerAnimationTiming.Clip(twice));
        Assert.Equal(new MediaTime(0), track.Keyframes[0].Time);
        Assert.Equal(new MediaTime(8), track.Keyframes[^1].Time);
    }

    [Fact]
    public void EntirelyExcludedKeysBecomeOneConstantBoundaryKey()
    {
        var layer = new ProjectLayer
        {
            Start = new(3), End = new(5), AnimationOffset = new(3),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.2), new(new(1), 0.8)])]
        };
        var clipped = LayerAnimationTiming.Clip(layer);
        var frame = Assert.Single(Assert.Single(clipped.Tracks).Keyframes);
        Assert.Equal(new MediaTime(3), frame.Time);
        Assert.Equal(0.8, frame.Value.Scalar);
    }

    [Fact]
    public void EndKeyIsLegalButOutOfRangeSnapshotIsRejected()
    {
        var layer = new ProjectLayer
        {
            Start = new(10), End = new(12), AnimationOffset = new(5),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(5), 0), new(new(7), 1)])]
        };
        ProjectValidator.Validate(new() { Layers = [layer] });
        Assert.Equal(new MediaTime(5), LayerAnimationTiming.ClampTime(layer, new(0)));
        Assert.Equal(new MediaTime(7), LayerAnimationTiming.ClampTime(layer, new(100)));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [layer with
        {
            Tracks = [new(AnimationProperty.OPACITY, [new(new(8), 1)])]
        }] }));
    }

    [Theory]
    [InlineData(KeyframeInterpolation.EASE_IN)]
    [InlineData(KeyframeInterpolation.EASE_OUT)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT)]
    public void VerySmallCurveSubrangesStayFiniteAndMonotonic(KeyframeInterpolation interpolation)
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY,
            [new(new(0), 0, interpolation) { CurveStart = 1 - 1e-15, CurveEnd = 1 }, new(new(1), 1)]);
        var previous = 0d;
        for (var sample = 0; sample <= 20; sample++)
        {
            var value = SceneEvaluator.EvaluateScalarTrack(track, new(sample, 20));
            Assert.True(double.IsFinite(value));
            Assert.InRange(value, previous, 1);
            previous = value;
        }
    }
}
