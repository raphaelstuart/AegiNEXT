using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineClipEditingWorkflowTests
{
    [Fact]
    public async Task CopyPasteAndDeleteUseWholeSelectionAndIndependentTransactions()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[1], ids);

        await session.CopyTimelineClipsAsync(ids[1], ids);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        await session.PasteTimelineClipsAsync(new(10));
        var pasted = context.Editor.Snapshot;
        Assert.Equal(4, pasted.Layers.Length);
        Assert.Equal(new MediaTime(10), pasted.Subtitles[2].Start);
        Assert.Equal(new MediaTime(13), pasted.Subtitles[3].Start);
        Assert.Equal(pasted.Layers.Skip(2).Select(layer => layer.Id).Order(), session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(pasted.Layers[3].Id, session.SelectedLayerId);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        session.SelectLayer(ids[1], ids);
        await session.DeleteTimelineClipsAsync(ids);
        Assert.Empty(context.Editor.Snapshot.Subtitles);
        Assert.Empty(context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task InvalidDraftAndStaleMenuCannotEditOrReplaceClipboard()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        var row = session.ViewModel.Subtitles.Rows[0];
        row.StartText = "invalid";
        await session.CopyTimelineClipsAsync(ids[0], ids);
        Assert.False(session.CanPasteTimelineClips);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal("invalid", row.StartText);
        row.Accept(document.Subtitles[0]);

        context.Editor.ShiftClips(ids, new(1));
        var changed = context.Editor.Snapshot;
        await session.DeleteTimelineClipsAsync(ids, document);
        Assert.Same(changed, context.Editor.Snapshot);
        Assert.True(context.Editor.Undo());
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task PasteCollisionIsLocalizedAndPreservesSnapshotSelectionAndHistory()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        await session.CopyTimelineClipsAsync(ids[0], ids);

        await session.PasteTimelineClipsAsync(new(1));

        var error = Assert.IsType<InvalidOperationException>(session.LastError);
        Assert.Equal(Localization.Get("Workbench.TimelinePasteFailed"), error.Message);
        Assert.NotNull(error.InnerException);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(ids.Order(), session.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task SaveAndSaveAsRetainClipboardButReopeningTheSameProjectClearsIt()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        await session.CopyTimelineClipsAsync(ids[0], ids);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "saved.aeginext");

        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        Assert.Null(session.LastError);
        Assert.True(session.CanPasteTimelineClips);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "saved-as.aeginext");
        await session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.Null(session.LastError);
        Assert.True(session.CanPasteTimelineClips);

        var reopened = await session.OpenProjectAsync(context.Dialogs.SavePath);

        Assert.Equal(ProjectOpenStatus.OPENED, reopened.Status);
        Assert.Equal(document.Id, session.DocumentSnapshot.Id);
        Assert.False(session.CanPasteTimelineClips);
        await session.PasteTimelineClipsAsync(new(10));
        Assert.Equal(2, context.Editor.Snapshot.Layers.Length);
        Assert.False(context.Editor.CanUndo);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewProjectAndSessionDisposalReleaseTheCapturedClipboard(bool dispose)
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var session = context.Session;
        var ids = document.Layers.Select(layer => layer.Id).ToArray();
        session.SelectLayer(ids[0], ids);
        await session.CopyTimelineClipsAsync(ids[0], ids);
        Assert.True(session.CanPasteTimelineClips);

        if (dispose)
        {
            await session.DisposeAsync();
        }
        else
        {
            var created = await session.CreateProjectAsync(Path.Combine(context.DirectoryPath, "created.aeginext"));
            Assert.Equal(ProjectOpenStatus.OPENED, created.Status);
            context.Editor.Reset(document);
        }

        Assert.False(session.CanPasteTimelineClips);
    }

    private static ProjectDocument CreateDocument()
    {
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "First" };
        var second = new SubtitleLine { Start = new(4), End = new(6), Text = "Second" };
        return new()
        {
            Subtitles = [first, second],
            Layers = [Layer(first), Layer(second)]
        };
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };
}
