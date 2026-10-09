using System.Text.Json;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class AnimationTrackTargetTests
{
    [Fact]
    public void OrdinaryTrackConstructorPreservesPropertyAndUsesNodeFreeTarget()
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.5)]);

        Assert.Equal(new AnimationTrackTarget(AnimationProperty.OPACITY), track.Target);
        Assert.Equal(AnimationProperty.OPACITY, track.Property);
        Assert.Null(track.Target.NodeId);
        ProjectValidator.Validate(new() { Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Tracks = [track] }] });
    }

    [Fact]
    public void TrackSerializationWritesOnlyCanonicalTargetAndRoundTripsKeyframes()
    {
        var track = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.POSITION),
        [
            new(new(0), new ScenePoint(10, 20), KeyframeInterpolation.EASE_IN)
            {
                CurveStart = 0.1,
                CurveEnd = 0.9,
                ComponentCurves = [new(KeyframeInterpolation.EASE_OUT, 0.2, 0.8)]
            }
        ]);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var encoded = JsonSerializer.Serialize(track, options);
        var restored = Assert.IsType<AnimationTrack>(JsonSerializer.Deserialize<AnimationTrack>(encoded, options));
        using var json = JsonDocument.Parse(encoded);

        Assert.True(json.RootElement.TryGetProperty("target", out var target));
        Assert.True(target.TryGetProperty("property", out _));
        Assert.False(json.RootElement.TryGetProperty("property", out _));
        Assert.Equal(track.Target, restored.Target);
        Assert.Equal(track.Keyframes.Length, restored.Keyframes.Length);
        for (var index = 0; index < track.Keyframes.Length; index++)
        {
            var expected = track.Keyframes[index];
            var actual = restored.Keyframes[index];
            Assert.Equal(expected.Time, actual.Time);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(expected.Interpolation, actual.Interpolation);
            Assert.Equal(expected.CurveStart, actual.CurveStart);
            Assert.Equal(expected.CurveEnd, actual.CurveEnd);
            Assert.Equal(expected.ComponentCurves.AsEnumerable(), actual.ComponentCurves.AsEnumerable());
            for (var component = 0; component < expected.Value.ComponentCount; component++)
            {
                Assert.Equal(expected.GetCurve(component), actual.GetCurve(component));
            }
        }
    }

    [Fact]
    public void CompleteTargetIdentityIncludesTheNodeId()
    {
        var first = new AnimationTrackTarget(AnimationProperty.POSITION, Guid.NewGuid());
        var second = first with { NodeId = Guid.NewGuid() };

        Assert.NotEqual(first, second);
        Assert.NotEqual(first, new AnimationTrackTarget(AnimationProperty.POSITION));
        Assert.Equal(first, first with { });
    }

    [Fact]
    public void OrdinaryAnimationPropertiesRejectAnyNodeTarget()
    {
        var track = new AnimationTrack(new AnimationTrackTarget(AnimationProperty.OPACITY, Guid.NewGuid()),
            [new(new(0), 0.5)]);

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new() { Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Tracks = [track] }] }));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Tracks = [track with { Target = track.Target with { NodeId = Guid.Empty } }] }]
        }));
    }

    [Fact]
    public void DuplicateCompleteTrackTargetsAreRejected()
    {
        var track = new AnimationTrack(AnimationProperty.OPACITY, [new(new(0), 0.5)]);

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 1, 1), Tracks = [track, track with { Keyframes = [new(new(0), 0.75)] }] }]
        }));
    }
}
