using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class ProjectClipIndexTests
{
    [Fact]
    public void TrackOrderDeterminesDrawingOrderRegardlessOfClipStorageOrder()
    {
        var bottom = new ProjectTrack { Name = "Bottom" };
        var front = Shape(ProjectTrack.DEFAULT_TRACK_ID);
        var back = Shape(bottom.Id);
        var original = new ProjectDocument { Tracks = [ProjectTrack.Default, bottom], Layers = [front, back] };
        var index = new ProjectClipIndex(original);
        Assert.Equal(new[] { back.Id, front.Id }, index.LayersInDrawingOrder.Select(clip => clip.Id));
        Assert.Same(front, index.GetClip(front.Id));
        Assert.True(index.TryGetClip(back.Id, out var found));
        Assert.Same(back, found);
        Assert.False(index.TryGetClip(Guid.NewGuid(), out _));
        var reordered = original with { Tracks = [bottom, ProjectTrack.Default] };
        Assert.Equal(new[] { front.Id, back.Id }, SceneEvaluator.Evaluate(reordered, new(1)).Select(clip => clip.Source.Id));
        Assert.Same(original, index.Document);
        Assert.Same(front, original.Layers[0]);
    }

    [Fact]
    public void MixedClipCollisionIsRejectedAndHalfOpenTouchingIsAllowed()
    {
        var subtitle = new SubtitleLine { Text = "Text", End = new(2) };
        var text = new ProjectLayer { SubtitleId = subtitle.Id, End = subtitle.End };
        var shape = Shape(ProjectTrack.DEFAULT_TRACK_ID) with { Start = new(2), End = new(3) };
        var project = new ProjectDocument { Subtitles = [subtitle], Layers = [text, shape] };
        ProjectValidator.Validate(project);
        var index = new ProjectClipIndex(project);
        Assert.Equal(new[] { text, shape }, index.GetTrackClips(text.TrackId));
        Assert.Equal(text.TrackId, index.GetSubtitleTrackId(subtitle.Id));
        Assert.Same(subtitle, Assert.Single(index.GetTrackSubtitles(text.TrackId)));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(project with
        {
            Layers = [text, shape with { Start = new(1) }]
        }));
    }

    private static ProjectLayer Shape(Guid trackId) => new()
    {
        TrackId = trackId, Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 4, 4), End = new(2)
    };
}
