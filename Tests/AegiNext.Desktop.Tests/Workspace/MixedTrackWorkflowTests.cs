using AegiNext.Application;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class MixedTrackWorkflowTests
{
    [Fact]
    public async Task ShapeSelectionSynchronizesTheTrackAndSubtitleListWithoutSelectingATextPayload()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var shape = document.Layers[1];

        context.Session.SelectLayer(shape.Id, [shape.Id]);

        Assert.Equal(shape.TrackId, context.Session.CurrentTrackId);
        Assert.Equal(shape.TrackId, context.Session.ViewModel.Timeline.SelectedTrackId);
        Assert.Equal(shape.TrackId, context.Session.ViewModel.Subtitles.SelectedTrack!.Id);
        Assert.Empty(context.Session.ViewModel.Subtitles.VisibleRows);
        Assert.Null(context.Session.SelectedCue);
        Assert.Empty(context.Session.SelectedSubtitleIds);
        Assert.Same(shape, context.Session.SelectedLayer);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ReorderingMixedTracksPreservesClipOwnershipAndHasOneUndoRedoAndPersistence()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var shape = document.Layers[1];
        context.Session.SelectLayer(shape.Id, [shape.Id]);

        await context.Session.MoveTrackAsync(shape.TrackId, 0, document);

        Assert.Null(context.Session.LastError);
        var changed = context.Editor.Snapshot;
        Assert.Equal(shape.TrackId, changed.Tracks[0].Id);
        Assert.Equal(document.Layers, changed.Layers);
        Assert.Equal(document.Subtitles, changed.Subtitles);
        Assert.Equal(shape.TrackId, context.Session.CurrentTrackId);
        Assert.Same(shape, context.Session.SelectedLayer);
        Assert.Equal(document.Tracks[0].Id, context.Session.ClipIndex.LayersInDrawingOrder[0].TrackId);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(changed));
        Assert.Equal(ProjectStore.Serialize(changed), ProjectStore.Serialize(restored));
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(changed, context.Editor.Snapshot);
    }

    [Fact]
    public async Task StaleSnapshotAndInvalidDraftRejectTrackReorderWithoutAddingHistory()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var row = context.Session.ViewModel.Subtitles.Rows[0];
        row.StartText = "invalid";
        await context.Session.MoveTrackAsync(document.Tracks[1].Id, 0, document);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal("invalid", row.StartText);
        Assert.False(context.Editor.CanUndo);
        row.Accept(document.Subtitles[0]);
        context.Editor.Apply("Change project", value => value with { Name = "Changed" });
        var changed = context.Editor.Snapshot;

        await context.Session.MoveTrackAsync(document.Tracks[1].Id, 0, document);

        Assert.Same(changed, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ShapeCanMoveToSubtitleTrackAtATouchingBoundaryButOverlapRejectsAtomically()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var shape = document.Layers[1];
        var target = document.Tracks[0].Id;

        await context.Session.CommitClipMoveAsync(shape.Id, target, new(1), new(3), TimelineEditMode.CROP, true, document);

        Assert.IsType<InvalidDataException>(context.Session.LastError);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        await context.Session.CommitClipMoveAsync(shape.Id, target, new(2), new(4), TimelineEditMode.CROP, true, document);
        Assert.Null(context.Session.LastError);
        var moved = context.Editor.Snapshot;
        Assert.Equal(target, context.Session.ClipIndex.GetClip(shape.Id).TrackId);
        Assert.Equal(target, context.Session.CurrentTrackId);
        Assert.Equal(shape.Id, context.Session.SelectedLayerId);
        Assert.Null(context.Session.SelectedCue);
        Assert.Single(context.Session.ViewModel.Subtitles.VisibleRows);
        Assert.Same(document.Subtitles[0], moved.Subtitles[0]);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task MixedClipboardUsesTheTargetTrackForEveryClipKind()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var ids = document.Layers.Select(clip => clip.Id).ToArray();
        context.Session.SelectLayer(ids[0], ids);
        await context.Session.CopyTimelineClipsAsync(ids[0], ids);

        await context.Session.PasteTimelineClipsAtTargetAsync(new(document.Tracks[0].Id, null, new(10)), document);

        Assert.Null(context.Session.LastError);
        var pasted = context.Editor.Snapshot;
        var drawingOrder = new ProjectClipIndex(document).LayersInDrawingOrder;
        Assert.Equal(drawingOrder.Select(clip => clip.TrackId), pasted.Layers.Skip(2).Select(clip => clip.TrackId));
        Assert.Equal(drawingOrder.Select(clip => clip.Kind), pasted.Layers.Skip(2).Select(clip => clip.Kind));
        Assert.Equal(ids.Length, context.Session.ViewModel.Timeline.SelectedLayerIds.Count);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument CreateDocument()
    {
        var track = new ProjectTrack { Name = "Shapes" };
        var cue = new SubtitleLine { Start = MediaTime.Zero, End = new(2), Text = "Subtitle" };
        return new()
        {
            Tracks = [ProjectTrack.Default, track],
            Subtitles = [cue],
            Layers =
            [
                new() { Id = cue.Id, SubtitleId = cue.Id, Start = cue.Start, End = cue.End },
                new() { TrackId = track.Id, Kind = LayerKind.SHAPE, Start = new(3), End = new(5),
                    Shape = new(ShapeKind.RECTANGLE, 20, 30) }
            ]
        };
    }
}
