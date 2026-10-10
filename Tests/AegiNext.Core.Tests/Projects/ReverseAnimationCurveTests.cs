using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class ReverseAnimationCurveTests
{
    [Theory]
    [InlineData(KeyframeInterpolation.LINEAR, 1)]
    [InlineData(KeyframeInterpolation.HOLD, 1)]
    [InlineData(KeyframeInterpolation.EASE_IN, 1)]
    [InlineData(KeyframeInterpolation.EASE_OUT, 1)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT, 1)]
    [InlineData(KeyframeInterpolation.POWER, 0.125)]
    [InlineData(KeyframeInterpolation.POWER, 2.5)]
    [InlineData(KeyframeInterpolation.POWER, 12.5)]
    public void ReversedEndpointValuesAndCurveMatchActualTimeReversal(KeyframeInterpolation interpolation, double exponent)
    {
        var forward = new AnimationTrack(AnimationProperty.ROTATION,
            [new(new(0), 12, interpolation) { Exponent = exponent }, new(new(4), 212)]);
        var backward = new AnimationTrack(AnimationProperty.ROTATION,
            [new(new(0), 212, interpolation) { Exponent = exponent, Reverse = true }, new(new(4), 12)]);

        for (var index = 0; index <= 80; index++)
        {
            var time = new MediaTime(index, 20);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(forward, new MediaTime(4) - time),
                SceneEvaluator.EvaluateScalarTrack(backward, time), 10);
        }
    }

    [Fact]
    public void ReverseHoldKeepsExactInteriorKeyValuesAndJumpsOnlyAfterTheKey()
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(0), 20, KeyframeInterpolation.HOLD) { Reverse = true },
            new(new(3), 9, KeyframeInterpolation.HOLD) { Reverse = true },
            new(new(5), 2)
        ]);

        Assert.Equal(20, SceneEvaluator.EvaluateScalarTrack(track, new(0)));
        Assert.Equal(9, SceneEvaluator.EvaluateScalarTrack(track, new(1, 1000)));
        Assert.Equal(9, SceneEvaluator.EvaluateScalarTrack(track, new(3)));
        Assert.Equal(2, SceneEvaluator.EvaluateScalarTrack(track, new(3001, 1000)));
        Assert.Equal(2, SceneEvaluator.EvaluateScalarTrack(track, new(5)));
        var clipped = LayerAnimationTiming.Clip(Layer(track) with { Start = new(1), End = new(4), AnimationOffset = new(1) });
        Assert.Equal(9, SceneEvaluator.EvaluateScalarTrack(clipped.Tracks[0], new(3)));
        Assert.Equal(2, SceneEvaluator.EvaluateScalarTrack(clipped.Tracks[0], new(3001, 1000)));
    }

    [Theory]
    [InlineData(0.125)]
    [InlineData(1.75)]
    [InlineData(12.5)]
    public void RepeatedCroppingKeepsReversedPowerSamplesAndDirection(double exponent)
    {
        var track = new AnimationTrack(AnimationProperty.ROTATION,
        [
            new(new(0), 12, KeyframeInterpolation.POWER)
            {
                CurveStart = 0.1, CurveEnd = 0.9, Exponent = exponent, Reverse = true
            },
            new(new(8), 212)
        ]);
        var once = LayerAnimationTiming.Clip(Layer(track) with { Start = new(1), End = new(7), AnimationOffset = new(1) });
        var twice = LayerAnimationTiming.Clip(once with { Start = new(2), End = new(6), AnimationOffset = new(2) });

        Assert.True(twice.Tracks[0].Keyframes[0].Reverse);
        Assert.Equal(exponent, twice.Tracks[0].Keyframes[0].Exponent);
        for (var index = 0; index <= 80; index++)
        {
            var time = new MediaTime(40 + index, 20);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(track, time), SceneEvaluator.EvaluateScalarTrack(twice.Tracks[0], time), 9);
        }
    }

    [Fact]
    public void IndependentComponentDirectionSurvivesCroppingAndGetCurveInheritance()
    {
        var track = new AnimationTrack(AnimationProperty.POSITION,
        [
            new(new(0), new ScenePoint(10, 20), KeyframeInterpolation.EASE_IN)
            {
                Reverse = true,
                ComponentCurves = [new(KeyframeInterpolation.POWER, 0.2, 0.8) { Exponent = 3.5, Reverse = false }]
            },
            new(new(8), new ScenePoint(110, 220))
        ]);
        var clipped = LayerAnimationTiming.Clip(Layer(track) with { Start = new(2), End = new(6), AnimationOffset = new(2) });

        Assert.True(clipped.Tracks[0].Keyframes[0].GetCurve(0).Reverse);
        Assert.False(clipped.Tracks[0].Keyframes[0].GetCurve(1).Reverse);
        for (var index = 0; index <= 40; index++)
        {
            var time = new MediaTime(20 + index, 10);
            var expected = SceneEvaluator.EvaluateVectorTrack(track, time);
            var actual = SceneEvaluator.EvaluateVectorTrack(clipped.Tracks[0], time);
            Assert.Equal(expected.X, actual.X, 10);
            Assert.Equal(expected.Y, actual.Y, 10);
        }
    }

    [Fact]
    public void ReversedPowerComponentKeepsItsDirectionWhenTheMainCurveIsForward()
    {
        var track = new AnimationTrack(AnimationProperty.SCALE,
        [
            new(new(0), new ScenePoint(1, 1), KeyframeInterpolation.LINEAR)
            {
                ComponentCurves = [new(KeyframeInterpolation.POWER) { Exponent = 2, Reverse = true }]
            },
            new(new(4), new ScenePoint(3, 5))
        ]);
        var sample = SceneEvaluator.EvaluateVectorTrack(track, new(1));
        var clipped = LayerAnimationTiming.Clip(Layer(track) with { Start = new(1), End = new(3), AnimationOffset = new(1) });

        Assert.Equal(1.5, sample.X, 12);
        Assert.Equal(2.75, sample.Y, 12);
        Assert.True(clipped.Tracks[0].Keyframes[0].GetCurve(1).Reverse);
        Assert.Equal(sample, SceneEvaluator.EvaluateVectorTrack(clipped.Tracks[0], new(1)));
    }

    [Fact]
    public void LegacyChannelMergePreservesBothReversedCurvesAcrossTheTimeUnion()
    {
        var x = new AnimationTrack(AnimationProperty.POSITION_X,
            [new(new(0), 0, KeyframeInterpolation.POWER) { Exponent = 2.5, Reverse = true }, new(new(8), 80)]);
        var y = new AnimationTrack(AnimationProperty.POSITION_Y,
        [
            new(new(0), 10, KeyframeInterpolation.EASE_IN) { Reverse = true },
            new(new(3), 40, KeyframeInterpolation.POWER) { Exponent = 0.7, Reverse = true },
            new(new(8), 90)
        ]);

        var merged = Assert.Single(LegacyAnimationTrackMigration.Merge([x, y], AnimationProperty.POSITION, new ScenePoint(0, 0)));

        Assert.True(merged.Keyframes[0].Reverse);
        Assert.True(merged.Keyframes[0].GetCurve(1).Reverse);
        Assert.True(merged.Keyframes[1].GetCurve(1).Reverse);
        for (var index = 0; index <= 80; index++)
        {
            var time = new MediaTime(index, 10);
            var actual = SceneEvaluator.EvaluateVectorTrack(merged, time);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(x, time), actual.X, 9);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(y, time), actual.Y, 9);
        }
    }

    private static ProjectLayer Layer(AnimationTrack track)
    {
        return new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), End = new(8), Tracks = [track] };
    }
}
