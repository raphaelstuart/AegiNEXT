using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class MixedTrackEditingTests
{
    private static readonly string[] mergedTexts = ["target", "source", "source"];
    [Fact]
    public void GenericMoveRetainsEffectsAndTrackOwnsTheSubtitleExactlyOnce()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(2), "subtitle");
        var target = editor.AddTrack("Mixed");
        var shape = new ProjectLayer
        {
            TrackId = target, Kind = LayerKind.SHAPE, Start = new(2), End = new(4),
            Shape = new(ShapeKind.RECTANGLE, 10, 10), AnimationOffset = new(1, 3)
        };
        editor.AddLayer(shape);
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1), 0.5));
        var before = editor.Snapshot;
        editor.Reset(before);
        var changes = 0;
        editor.Changed += (_, _) => changes++;

        editor.MoveClip(id, target, new(4), new(6), TimelineEditMode.CROP, true);

        var index = new ProjectClipIndex(editor.Snapshot);
        Assert.Equal(target, index.GetSubtitleTrackId(id));
        Assert.Equal(new MediaTime(4), Assert.Single(editor.Snapshot.Subtitles).Start);
        Assert.Equal(before.Layers[0].Tracks, index.GetSubtitleClip(id).Tracks);
        Assert.Equal(2, index.GetTrackClips(target).Length);
        Assert.Equal(1, changes);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingOrStretchingAVisualClipIntoSubtitleRejectsTheWholeTransaction(bool move)
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "subtitle");
        var track = editor.AddTrack("Shape");
        var shape = new ProjectLayer { TrackId = track, Kind = LayerKind.SHAPE, Start = new(0), End = new(2),
            Shape = new(ShapeKind.RECTANGLE, 10, 10) };
        editor.AddLayer(shape);
        var before = editor.Snapshot;

        Assert.Throws<InvalidDataException>(() => editor.MoveClip(shape.Id, ProjectTrack.DEFAULT_TRACK_ID,
            new(1), new(3), TimelineEditMode.STRETCH, move));

        Assert.Same(before, editor.Snapshot);
        Assert.Equal(track, editor.Snapshot.Layers[1].TrackId);
    }

    [Fact]
    public void VisualClipCropKeepsItsAbsoluteContentOriginAndCanTouchASubtitleBoundary()
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(2), "subtitle");
        var shape = new ProjectLayer { Kind = LayerKind.SHAPE, Start = new(3), End = new(5),
            Shape = new(ShapeKind.RECTANGLE, 10, 10), AnimationOffset = new(1, 4) };
        editor.AddLayer(shape);

        editor.MoveClip(shape.Id, ProjectTrack.DEFAULT_TRACK_ID, new(2), new(5), TimelineEditMode.CROP, false);

        var result = editor.Snapshot.Layers[1];
        Assert.Equal(shape.Start - shape.AnimationOffset, result.Start - result.AnimationOffset);
        Assert.Equal(new MediaTime(-3, 4), result.AnimationOffset);
    }

    [Fact]
    public void TrackOrderAndMergeKeepLaterImportedProjectsAboveTheOriginal()
    {
        var target = new ProjectEditor();
        target.AddSubtitle(new(0), new(2), "target");
        var source = new ProjectEditor();
        source.AddSubtitle(new(0), new(2), "source");
        var merged = ProjectEditingOperations.MergeProjects(target.Snapshot,
            [new(source.Snapshot, "A", "/a"), new(source.Snapshot, "B", "/b")]);

        Assert.Equal(mergedTexts,
            SceneEvaluator.Evaluate(merged.Document, new(1)).Select(clip => clip.Subtitle!.Text));
        Assert.Equal(new[] { merged.ImportedTrackIds[1], merged.ImportedTrackIds[0], ProjectTrack.DEFAULT_TRACK_ID },
            merged.Document.Tracks.Select(track => track.Id));
        var reordered = ProjectEditingOperations.MoveTrack(merged.Document, ProjectTrack.DEFAULT_TRACK_ID, 0);
        Assert.Equal("target", SceneEvaluator.Evaluate(reordered, new(1))[^1].Subtitle!.Text);
        Assert.Equal(merged.Document.Layers, reordered.Layers);
    }

    [Fact]
    public void PreparedSubtitleBatchUsesAnExistingTrackAfterTheOriginalDefaultWasDeleted()
    {
        var track = new ProjectTrack { Name = "Remaining" };
        var editor = new ProjectEditor(new() { Tracks = [track] });
        var line = new SubtitleLine { Start = new(0), End = new(2), Text = "prepared", Style = new() { FontSize = 37 } };

        editor.AddSubtitles([line]);

        Assert.Equal(track.Id, Assert.Single(editor.Snapshot.Layers).TrackId);
        Assert.Same(line.Style, Assert.Single(editor.Snapshot.Subtitles).Style);
        Assert.True(editor.Undo());
        Assert.Empty(editor.Snapshot.Layers);
    }
}
