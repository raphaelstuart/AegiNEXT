using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class VectorAnimationTests
{
    [Fact]
    public void SingleVectorTrackEvaluatesBothComponentsAndKeepsTheirDimension()
    {
        var track = new AnimationTrack(AnimationProperty.POSITION,
            [new(new(0), new ScenePoint(10, -20)), new(new(2), new ScenePoint(30, 40))]);
        var layer = new ProjectLayer { End = new(2), Transform = new() { Position = new(500, 600) }, Tracks = [track] };
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(new ProjectDocument { Layers = [layer] }, new(1)));
        Assert.Equal(new ScenePoint(20, 10), evaluated.Transform.Position);
        Assert.Equal(new ScenePoint(10, -20), SceneEvaluator.EvaluateVectorTrack(track, new(-1)));
        Assert.Equal(new ScenePoint(30, 40), SceneEvaluator.EvaluateVectorTrack(track, new(3)));
        Assert.Throws<InvalidOperationException>(() => SceneEvaluator.EvaluateScalarTrack(track, new(1)));
        Assert.Throws<InvalidOperationException>(() => AnimationValue.FromScalar(1).AsVector());
    }

    [Theory]
    [InlineData(KeyframeInterpolation.HOLD)]
    [InlineData(KeyframeInterpolation.LINEAR)]
    [InlineData(KeyframeInterpolation.EASE_IN)]
    [InlineData(KeyframeInterpolation.EASE_OUT)]
    [InlineData(KeyframeInterpolation.EASE_IN_OUT)]
    public void RepeatedVectorCroppingPreservesBothComponentCurvePhases(KeyframeInterpolation interpolation)
    {
        var track = new AnimationTrack(AnimationProperty.POSITION,
        [
            new(new(0), new ScenePoint(10, -100), interpolation)
            {
                VectorCurve = new(KeyframeInterpolation.EASE_OUT)
            },
            new(new(8), new ScenePoint(210, 300))
        ]);
        var once = LayerAnimationTiming.Clip(new() { Start = new(1), End = new(7), AnimationOffset = new(1), Tracks = [track] });
        var twice = LayerAnimationTiming.Clip(once with { Start = new(2), End = new(6), AnimationOffset = new(2) });
        var clipped = Assert.Single(twice.Tracks);
        ProjectValidator.Validate(new() { Layers = [twice] });
        for (var sample = 0; sample <= 80; sample++)
        {
            var time = new MediaTime(40 + sample, 20);
            var expected = SceneEvaluator.EvaluateVectorTrack(track, time);
            var actual = SceneEvaluator.EvaluateVectorTrack(clipped, time);
            Assert.Equal(expected.X, actual.X, 9);
            Assert.Equal(expected.Y, actual.Y, 9);
        }
    }

    [Fact]
    public void DimensionsAndLegacyComponentTracksCannotEnterTheAuthoritativeSnapshot()
    {
        var scalarPosition = new ProjectLayer { Tracks = [new(AnimationProperty.POSITION, [new(MediaTime.Zero, 1)])] };
        var vectorOpacity = new ProjectLayer { Tracks = [new(AnimationProperty.OPACITY, [new(MediaTime.Zero, new ScenePoint(0, 1))])] };
        var legacy = new ProjectLayer { Tracks = [new(AnimationProperty.POSITION_X, [new(MediaTime.Zero, 1)])] };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [scalarPosition] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [vectorOpacity] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [legacy] }));
    }
}
