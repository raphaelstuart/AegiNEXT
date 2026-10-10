using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class SubtitleAnimationTargetEditingTests
{
    [Fact]
    public void EditingRangeAnimationKeepsWholeLineAndOtherStateUntouched()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "字幕", End = new(2), AnimationRanges = [range] };
        var whole = new AnimationTrack(AnimationProperty.FILL, [new(new(1), SceneColor.White)]);
        var normalTarget = new AnimationTrackTarget(AnimationProperty.FILL, TextRangeId: range.Id);
        var inactiveTarget = normalTarget with { State = SubtitleAnimationState.INACTIVE };
        var inactive = new AnimationTrack(inactiveTarget, [new(new(1), SceneColor.Black)]);
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End, Tracks = [whole, inactive] };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var target = new AnimationEditTarget(layer.Id, new(1), true, normalTarget);

        var result = AnimationEditOperations.SetValue(source, target, AnimationProperty.FILL, new SceneColor(0.5, 0.25, 0.75, 0.6));

        Assert.Same(whole, result.Layers[0].Tracks[0]);
        Assert.Same(inactive, result.Layers[0].Tracks[1]);
        Assert.Equal(normalTarget, result.Layers[0].Tracks[2].Target);
        Assert.Equal(new SceneColor(0.5, 0.25, 0.75, 0.6), result.Layers[0].Tracks[2].Keyframes[0].Value.Color);
        Assert.Same(line, result.Subtitles[0]);
    }

    [Fact]
    public void RangeTransformBaseEditKeepsWholeLayerTransform()
    {
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 0, 2);
        var line = new SubtitleLine { Text = "字幕", End = new(2), AnimationRanges = [range] };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, End = line.End };
        var source = new ProjectDocument { Subtitles = [line], Layers = [layer] };
        var target = new AnimationEditTarget(layer.Id, new(0), false,
            new(AnimationProperty.SCALE, TextRangeId: range.Id));

        var result = AnimationEditOperations.SetValue(source, target, AnimationProperty.SCALE, new ScenePoint(2, 3));

        Assert.Equal(new ScenePoint(2, 3), result.Subtitles[0].AnimationRanges[0].Scale);
        Assert.Equal(layer.Transform, result.Layers[0].Transform);
        Assert.Empty(result.Layers[0].Tracks);
    }

    [Fact]
    public void ReadingRangeValueDoesNotReadAnotherScopeTrack()
    {
        var range = Guid.NewGuid();
        var target = new AnimationTrackTarget(AnimationProperty.ROTATION, TextRangeId: range);
        var layer = new ProjectLayer
        {
            End = new(2), Tracks = [new(AnimationProperty.ROTATION, [new(new(1), 20)]), new(target, [new(new(1), 90)])]
        };
        Assert.Equal(90, AnimationEditOperations.Value(layer, AnimationProperty.ROTATION,
            new(layer.Id, new(1), true, target), 0));
    }
}
