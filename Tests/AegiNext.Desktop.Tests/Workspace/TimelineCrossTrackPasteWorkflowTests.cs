using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineCrossTrackPasteWorkflowTests
{
    [Fact]
    public async Task ContextTargetMovesWholeBatchToAnotherTrackWithOneUndoAndBatchSelection()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        var targetTrack = document.SubtitleTracks[1];
        session.SelectLayer(ids[1], ids);
        await session.CopyTimelineClipsAsync(ids[1], ids);
        var changes = 0;
        context.Editor.Changed += (_, _) => changes++;

        await session.PasteTimelineClipsAtTargetAsync(new TimelineClipContextEventArgs(targetTrack.Id, null, new(10)), document);

        Assert.Null(session.LastError);
        var pasted = context.Editor.Snapshot;
        var copies = pasted.Subtitles.Skip(document.Subtitles.Length).ToArray();
        Assert.Equal(2, copies.Length);
        Assert.All(copies, cue => Assert.Equal(targetTrack.Id, cue.TrackId));
        Assert.Equal(new MediaTime(10), copies[0].Start);
        Assert.Equal(new MediaTime(12), copies[1].Start);
        Assert.Equal(document.Subtitles.Select(cue => cue.End - cue.Start), copies.Select(cue => cue.End - cue.Start));
        Assert.Equal(document.Subtitles, pasted.Subtitles.Take(document.Subtitles.Length));
        Assert.Equal(document.Layers, pasted.Layers.Take(document.Layers.Length));
        Assert.Equal(pasted.Layers.Skip(document.Layers.Length).Select(layer => layer.Id).Order(),
            session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(pasted.Layers[^1].Id, session.SelectedLayerId);
        Assert.Equal(targetTrack.Id, session.CurrentTrackId);
        Assert.Equal(1, changes);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.Redo());
        Assert.Same(pasted, context.Editor.Snapshot);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task ShapePrimaryFreezesCurrentSourceTrackAndPreservesTrackGapsAfterHeaderSelection()
    {
        var sourceA = new SubtitleTrack { Name = "Source A" };
        var target = new SubtitleTrack { Name = "Target" };
        var sourceB = new SubtitleTrack { Name = "Source B" };
        var gap = new SubtitleTrack { Name = "Gap" };
        var destinationB = new SubtitleTrack { Name = "Destination B" };
        var first = new SubtitleLine { TrackId = sourceA.Id, Start = new(3), End = new(4), Text = "First" };
        var second = new SubtitleLine { TrackId = sourceB.Id, Start = new(5), End = new(6), Text = "Second" };
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Start = new(1), End = new(2), Shape = new(ShapeKind.RECTANGLE, 30, 40)
        };
        var document = new ProjectDocument
        {
            SubtitleTracks = [SubtitleTrack.Default, sourceA, target, sourceB, gap, destinationB],
            Subtitles = [first, second],
            Layers = [shape, Layer(first), Layer(second)]
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        Assert.True(session.SelectTrack(SubtitleTrack.DEFAULT_TRACK_ID));
        session.SelectLayer(shape.Id, ids);
        Assert.Equal(SubtitleTrack.DEFAULT_TRACK_ID, session.CurrentTrackId);
        await session.CopyTimelineClipsAsync(shape.Id, ids);

        Assert.True(session.SelectTrack(target.Id));
        Assert.Empty(session.ViewModel.Timeline.SelectedLayerIds);
        Assert.Null(session.SelectedLayerId);
        Assert.True(session.CanPasteTimelineClips);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        await session.PasteTimelineClipsAtTargetAsync(new TimelineClipContextEventArgs(target.Id, null, new(10)), document);

        Assert.Null(session.LastError);
        var pasted = context.Editor.Snapshot;
        var copiedShape = pasted.Layers[3];
        Assert.Equal(LayerKind.SHAPE, copiedShape.Kind);
        Assert.Equal(new MediaTime(10), copiedShape.Start);
        Assert.Equal(sourceB.Id, pasted.Subtitles[2].TrackId);
        Assert.Equal(destinationB.Id, pasted.Subtitles[3].TrackId);
        Assert.Equal(new MediaTime(12), pasted.Subtitles[2].Start);
        Assert.Equal(new MediaTime(14), pasted.Subtitles[3].Start);
        Assert.Equal(copiedShape.Id, session.SelectedLayerId);
        Assert.Equal(pasted.Layers.Skip(3).Select(layer => layer.Id).Order(), session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(target.Id, session.CurrentTrackId);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task NullTargetTrackRejectsSubtitleClipboardWithLocalizedErrorAndNoMutation()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[1], ids);
        await session.CopyTimelineClipsAsync(ids[1], ids);

        await session.PasteTimelineClipsAtTargetAsync(new TimelineClipContextEventArgs(null, null, new(10)), document);

        AssertPasteRejected(context, document, ids, ids[1]);
        Assert.True(session.CanPasteTimelineClips);
    }

    [Fact]
    public async Task GraphicsOnlyClipboardCanPasteToNullTrackTargetWithOneUndo()
    {
        var shape = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Start = new(1), End = new(3), Shape = new(ShapeKind.ELLIPSE, 30, 40),
            Opacity = 0.5, AnimationOffset = new(1, 3)
        };
        var document = new ProjectDocument { Layers = [shape] };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        session.SelectLayer(shape.Id, [shape.Id]);
        await session.CopyTimelineClipsAsync(shape.Id, [shape.Id]);

        await session.PasteTimelineClipsAtTargetAsync(new TimelineClipContextEventArgs(null, null, new(10)), document);

        Assert.Null(session.LastError);
        var pasted = context.Editor.Snapshot;
        Assert.Empty(pasted.Subtitles);
        var copy = pasted.Layers[1];
        Assert.NotEqual(shape.Id, copy.Id);
        Assert.Equal(shape with { Id = copy.Id, Start = new(10), End = new(12) }, copy);
        Assert.Equal(copy.Id, session.SelectedLayerId);
        Assert.Equal(new[] { copy.Id }, session.ViewModel.Timeline.SelectedLayerIds);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task CollisionOnTargetTrackRejectsEntireBatchAndPreservesSelectionAndHistory()
    {
        var source = CreateDocument();
        var obstacle = new SubtitleLine
        {
            TrackId = source.SubtitleTracks[1].Id, Start = new(12), End = new(13), Text = "Obstacle"
        };
        var document = source with
        {
            Subtitles = source.Subtitles.Add(obstacle), Layers = source.Layers.Add(Layer(obstacle))
        };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = source.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[1], ids);
        await session.CopyTimelineClipsAsync(ids[1], ids);

        await session.PasteTimelineClipsAtTargetAsync(new TimelineClipContextEventArgs(obstacle.TrackId, obstacle.Id, new(10)), document);

        AssertPasteRejected(context, document, ids, ids[1]);
        Assert.Equal(SubtitleTrack.DEFAULT_TRACK_ID, session.CurrentTrackId);
    }

    [Fact]
    public async Task StaleContextCannotPasteOrAddHistoryAfterAnotherDocumentEdit()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        await session.CopyTimelineClipsAsync(ids[0], ids);
        context.Editor.ShiftClips([ids[0]], new(1));
        var changed = context.Editor.Snapshot;
        var changes = 0;
        context.Editor.Changed += (_, _) => changes++;

        await session.PasteTimelineClipsAtTargetAsync(new TimelineClipContextEventArgs(document.SubtitleTracks[1].Id, null, new(10)), document);

        Assert.Null(session.LastError);
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.Equal(ids.Order(), session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(ids[0], session.SelectedLayerId);
        Assert.Equal(0, changes);
        Assert.True(session.CanPasteTimelineClips);
        Assert.False(context.Editor.CanRedo);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument CreateDocument()
    {
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        return new()
        {
            SubtitleTracks = [SubtitleTrack.Default, new() { Name = "Target" }],
            Subtitles = [first, second],
            Layers = [Layer(first), Layer(second)]
        };
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static void AssertPasteRejected(WorkspaceSessionTestContext context, ProjectDocument expected,
        IReadOnlyCollection<Guid> selectedIds, Guid primaryId)
    {
        var error = Assert.IsType<InvalidOperationException>(context.Session.LastError);
        Assert.Equal(Localization.Get("Workbench.TimelinePasteFailed"), error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Same(expected, context.Editor.Snapshot);
        Assert.Equal(selectedIds.Order(), context.Session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(primaryId, context.Session.SelectedLayerId);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }
}
