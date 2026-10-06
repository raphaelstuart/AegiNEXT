using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Projects;

public sealed class ColorAnimationTests
{
    [Fact]
    public void LinearHdrColorInterpolatesWithoutEncodingPremultiplicationOrClipping()
    {
        var track = new AnimationTrack(AnimationProperty.FILL,
            [new(new(0), new SceneColor(-2, 4, 12, 0)), new(new(4), new SceneColor(2, 8, 20, 1))]);
        var evaluated = Assert.Single(SceneEvaluator.Evaluate(new ProjectDocument
        {
            Layers = [new() { End = new(4), Tracks = [track] }]
        }, new(2)));
        Assert.Equal(new SceneColor(0, 6, 16, 0.5), evaluated.Fill);
        Assert.Equal(new SceneColor(-2, 4, 12, 0), SceneEvaluator.EvaluateColorTrack(track, new(-1)));
        Assert.Equal(new SceneColor(2, 8, 20, 1), SceneEvaluator.EvaluateColorTrack(track, new(4)));
        Assert.Throws<InvalidOperationException>(() => SceneEvaluator.EvaluateVectorTrack(track, new(2)));
        Assert.Equal(new SceneColor(0, 6, 16, 0.75), ((AnimationValue)evaluated.Fill).WithComponent(3, 0.75).Color);
    }

    [Fact]
    public void FourIndependentCurvesKeepExactSamplesAfterRepeatedCroppingAndStretching()
    {
        var track = new AnimationTrack(AnimationProperty.STROKE,
        [
            new(new(0), new SceneColor(-1, 2, 4, 0), KeyframeInterpolation.EASE_IN)
            {
                ComponentCurves = [new(KeyframeInterpolation.EASE_OUT), new(KeyframeInterpolation.HOLD), new(KeyframeInterpolation.EASE_IN_OUT)]
            },
            new(new(8), new SceneColor(3, 10, 12, 1))
        ]);
        var original = new ProjectLayer { End = new(8), Tracks = [track] };
        var once = LayerAnimationTiming.Clip(original with { Start = new(1), End = new(7), AnimationOffset = new(1) });
        var twice = LayerAnimationTiming.Clip(once with { Start = new(2), End = new(6), AnimationOffset = new(2) });
        var stretched = LayerAnimationTiming.Retime(twice, new(2), new(10), TimelineEditMode.STRETCH);
        ProjectValidator.Validate(new() { Layers = [twice] });
        ProjectValidator.Validate(new() { Layers = [stretched] });
        for (var sample = 0; sample <= 80; sample++)
        {
            var time = new MediaTime(40 + sample, 20);
            var expected = SceneEvaluator.EvaluateTrack(track, time);
            var cropped = SceneEvaluator.EvaluateTrack(Assert.Single(twice.Tracks), time);
            var scaled = SceneEvaluator.EvaluateTrack(Assert.Single(stretched.Tracks), new(time.Numerator * 2, time.Denominator));
            for (var component = 0; component < 4; component++)
            {
                Assert.Equal(expected.GetComponent(component), cropped.GetComponent(component), 9);
                Assert.Equal(expected.GetComponent(component), scaled.GetComponent(component), 9);
            }
        }
    }

    [Fact]
    public void ColorDimensionRangeAndCurveOverrideCountAreValidatedAtTheSnapshotBoundary()
    {
        var invalid = new Keyframe[]
        {
            new(new(0), 1), new(new(0), new ScenePoint(0, 1)),
            new(new(0), new SceneColor(0, 0, 0, 2)), new(new(0), new SceneColor(65505, 0, 0)),
            new(new(0), SceneColor.White) { ComponentCurves = [new(KeyframeInterpolation.EASE_IN)] },
            new(new(0), SceneColor.White) { ComponentCurves = [null, new(KeyframeInterpolation.LINEAR, 0.8, 0.2), null] }
        };
        foreach (var frame in invalid)
        {
            Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
            {
                Layers = [new() { Tracks = [new(AnimationProperty.FILL, [frame])] }]
            }));
        }

        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(new()
        {
            Layers = [new() { Tracks = [new(AnimationProperty.FILL_RED, [new(new(0), 1)])] }]
        }));
        Assert.Equal(19, (int)AnimationProperty.FILL);
        Assert.Equal(20, (int)AnimationProperty.STROKE);
        Assert.Equal(4, AnimationPropertyMetadata.GetComponentCount(AnimationProperty.FILL));
        Assert.Equal("A", AnimationPropertyMetadata.GetComponentName(AnimationProperty.FILL, 3));
    }

    [Fact]
    public void LegacyMergedColorAcceptsTheFullBoundedComponentTimeUnion()
    {
        var red = new AnimationTrack(AnimationProperty.FILL_RED,
            Enumerable.Range(0, 10000).Select(index => new Keyframe(new(index * 2), index % 10)).ToImmutableArray());
        var green = new AnimationTrack(AnimationProperty.FILL_GREEN,
            Enumerable.Range(0, 10000).Select(index => new Keyframe(new(index * 2 + 1), index % 20)).ToImmutableArray());
        var tracks = LegacyAnimationTrackMigration.Merge([red, green], AnimationProperty.FILL, new SceneColor(0, 0, 7, 0.75));
        Assert.Equal(20000, Assert.Single(tracks).Keyframes.Length);
        ProjectValidator.Validate(new() { Layers = [new() { End = new(20000), Tracks = tracks }] });
        Assert.Equal(7, SceneEvaluator.EvaluateColorTrack(tracks[0], new(19999)).Blue);
        Assert.Equal(0.75, SceneEvaluator.EvaluateColorTrack(tracks[0], new(19999)).Alpha);
    }
}
