using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineViewStatePersistenceTests
{
    [Theory]
    [InlineData(TimelineRowScope.SUBTITLE_TRACK)]
    [InlineData(TimelineRowScope.SCENE_LAYER)]
    public async Task InitializationRestoresCollapsedRowsWithoutChangingTheEditorSnapshotOrHistory(TimelineRowScope scope)
    {
        var document = CreateDocument();
        var row = GetRowId(document, scope);
        document = document with { TimelineViewState = new() { CollapsedAnimationRows = [row] } };
        await using var context = new WorkspaceSessionTestContext(document);

        await context.InitializeAsync();

        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.Equal(row, Assert.Single(context.Session.ViewModel.Timeline.TimelineViewState.CollapsedAnimationRows));
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Theory]
    [InlineData(TimelineRowScope.SUBTITLE_TRACK)]
    [InlineData(TimelineRowScope.SCENE_LAYER)]
    public async Task CollapseAndExpandPreserveTheContentSnapshotAndReturningToTheSavePointClearsViewDirty(TimelineRowScope scope)
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var row = GetRowId(document, scope);

        context.Session.SetTimelineAnimationRowCollapsed(row, true);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.Equal(row, Assert.Single(context.Session.ViewModel.Timeline.TimelineViewState.CollapsedAnimationRows));
        Assert.Empty(context.Editor.Snapshot.TimelineViewState.CollapsedAnimationRows);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);

        context.Session.SetTimelineAnimationRowCollapsed(row, true);

        Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows);
        Assert.Same(document, context.Editor.Snapshot);

        context.Session.SetTimelineAnimationRowCollapsed(row, false);

        Assert.Empty(context.Session.TimelineViewState.CollapsedAnimationRows);
        Assert.Empty(context.Session.ViewModel.Timeline.TimelineViewState.CollapsedAnimationRows);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task ViewChangesPreserveRedoAndContentUndoDoesNotUndoTheCollapsedRows()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var row = GetRowId(document, TimelineRowScope.SUBTITLE_TRACK);
        context.Editor.Apply("Resize project", value => value with { Width = 1280 });
        await context.Session.WaitForProjectIdleAsync();
        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();
        var redoLabel = context.Editor.RedoLabel;

        context.Session.SetTimelineAnimationRowCollapsed(row, true);

        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.True(context.Editor.CanRedo);
        Assert.Equal(redoLabel, context.Editor.RedoLabel);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(context.Editor.Redo());
        await context.Session.WaitForProjectIdleAsync();
        Assert.Equal(1280, context.Editor.Snapshot.Width);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();
        Assert.Same(document, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.True(context.Session.HasUnsavedChanges);

        context.Session.SetTimelineAnimationRowCollapsed(row, false);

        Assert.False(context.Session.HasUnsavedChanges);
        Assert.True(context.Editor.CanRedo);
        Assert.Equal(redoLabel, context.Editor.RedoLabel);
    }

    [Fact]
    public async Task ManualSaveAndSaveAsPersistViewStateAndReopeningRestoresEachProjectsOwnRows()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var originalPath = await OpenFixtureAsync(context, CreateDocument());
        var originalSnapshot = context.Editor.Snapshot;
        var subtitleRow = GetRowId(originalSnapshot, TimelineRowScope.SUBTITLE_TRACK);
        var sceneRow = GetRowId(originalSnapshot, TimelineRowScope.SCENE_LAYER);
        context.Session.SetTimelineAnimationRowCollapsed(subtitleRow, true);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(originalSnapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.Equal(subtitleRow, Assert.Single((await ProjectStore.LoadAsync(originalPath)).TimelineViewState.CollapsedAnimationRows));

        context.Session.SetTimelineAnimationRowCollapsed(sceneRow, true);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "relocated", "saved-as.aeginext");

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);

        Assert.Null(context.Session.LastError);
        Assert.Equal(context.Dialogs.SavePath, context.Session.ProjectPath);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Equal(new[] { subtitleRow, sceneRow }, (await ProjectStore.LoadAsync(context.Dialogs.SavePath)).TimelineViewState.CollapsedAnimationRows);

        var reopenedOriginal = await context.Session.OpenProjectAsync(originalPath);

        Assert.Equal(ProjectOpenStatus.OPENED, reopenedOriginal.Status);
        Assert.Equal(subtitleRow, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.False(context.Session.HasUnsavedChanges);
        var reopenedCopy = await context.Session.OpenProjectAsync(context.Dialogs.SavePath);
        Assert.Equal(ProjectOpenStatus.OPENED, reopenedCopy.Status);
        Assert.Equal(new[] { subtitleRow, sceneRow }, context.Session.TimelineViewState.CollapsedAnimationRows);
        Assert.Equal(new[] { subtitleRow, sceneRow }, context.Session.ViewModel.Timeline.TimelineViewState.CollapsedAnimationRows);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task ReturningToANonemptySavedViewStateClearsDirtyWithoutAContentEdit()
    {
        var document = CreateDocument();
        var row = GetRowId(document, TimelineRowScope.SCENE_LAYER);
        document = document with { TimelineViewState = new() { CollapsedAnimationRows = [row] } };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();

        context.Session.SetTimelineAnimationRowCollapsed(row, false);

        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Same(document, context.Editor.Snapshot);

        context.Session.SetTimelineAnimationRowCollapsed(row, true);

        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
    }

    [Fact]
    public async Task AutoSaveWritesViewOnlyChangesAndKeepsTheOriginalContentSnapshot()
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetRowId(snapshot, TimelineRowScope.SUBTITLE_TRACK);
        context.Session.SetTimelineAnimationRowCollapsed(row, true);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.True(context.Session.HasUnsavedChanges);

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        var saved = Assert.Single(storage.SavedSnapshots);
        Assert.Equal(row, Assert.Single(saved.TimelineViewState.CollapsedAnimationRows));
        Assert.Equal(snapshot.Layers, saved.Layers);
        Assert.Equal(snapshot.Subtitles, saved.Subtitles);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(storage.SavedSnapshots);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingViewDuringAutoSaveKeepsTheLaterStateDirtyAndSavesItOnTheNextTick(bool returnToPreviousSavePoint)
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var subtitleRow = GetRowId(snapshot, TimelineRowScope.SUBTITLE_TRACK);
        var sceneRow = GetRowId(snapshot, TimelineRowScope.SCENE_LAYER);
        context.Session.SetTimelineAnimationRowCollapsed(subtitleRow, true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (returnToPreviousSavePoint)
            {
                context.Session.SetTimelineAnimationRowCollapsed(subtitleRow, false);
            }
            else
            {
                context.Session.SetTimelineAnimationRowCollapsed(sceneRow, true);
            }

            release.TrySetResult();
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(subtitleRow, Assert.Single(Assert.Single(storage.SavedSnapshots).TimelineViewState.CollapsedAnimationRows));
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.False(context.Editor.HasUnsavedChanges);
            Assert.Same(snapshot, context.Editor.Snapshot);
            Assert.False(context.Editor.CanUndo);
            storage.SaveWork = null;
            clock.Advance(TimeSpan.FromMinutes(2));
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, storage.SavedSnapshots.Count);
            var expectedRows = returnToPreviousSavePoint ? Array.Empty<TimelineAnimationRowId>() : new[] { subtitleRow, sceneRow };
            Assert.Equal(expectedRows, storage.SavedSnapshots[1].TimelineViewState.CollapsedAnimationRows);
            Assert.Equal(expectedRows, context.Session.TimelineViewState.CollapsedAnimationRows);
            Assert.False(context.Session.HasUnsavedChanges);
            Assert.Same(snapshot, context.Editor.Snapshot);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task FailedAutoSaveKeepsViewDirtyAndTheNextTickRetriesTheSameCollapsedRows()
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetRowId(snapshot, TimelineRowScope.SCENE_LAYER);
        context.Session.SetTimelineAnimationRowCollapsed(row, true);
        var failure = new IOException("Timeline view state save failed");
        storage.SaveWork = _ => Task.FromException(failure);

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(failure, context.Session.LastError);
        Assert.Equal(1, storage.SaveAttempts);
        Assert.Empty(storage.SavedSnapshots);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        storage.SaveWork = null;

        clock.Advance(TimeSpan.FromMinutes(2));
        await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(2, storage.SaveAttempts);
        Assert.Equal(row, Assert.Single(Assert.Single(storage.SavedSnapshots).TimelineViewState.CollapsedAnimationRows));
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
    }

    [Fact]
    public async Task FailedManualSaveAsPreservesTheActiveProjectAndUnsavedCollapsedRows()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var originalPath = await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetRowId(snapshot, TimelineRowScope.SUBTITLE_TRACK);
        context.Session.SetTimelineAnimationRowCollapsed(row, true);
        var destination = Path.Combine(context.DirectoryPath, "directory.aeginext");
        Directory.CreateDirectory(destination);
        context.Dialogs.SavePath = destination;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);

        Assert.NotNull(context.Session.LastError);
        Assert.Equal(originalPath, context.Session.ProjectPath);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.Empty((await ProjectStore.LoadAsync(originalPath)).TimelineViewState.CollapsedAnimationRows);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task DecliningDiscardOfViewOnlyChangesKeepsTheActiveProjectAndCollapsedRows()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var originalPath = await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var row = GetRowId(snapshot, TimelineRowScope.SUBTITLE_TRACK);
        context.Session.SetTimelineAnimationRowCollapsed(row, true);
        var nextPath = Path.Combine(context.DirectoryPath, "next.aeginext");
        await ProjectStore.SaveAsync(CreateDocument(), nextPath);
        context.Dialogs.UnsavedChoice = 0;

        var result = await context.Session.OpenProjectAsync(nextPath);

        Assert.Equal(ProjectOpenStatus.CANCELLED, result.Status);
        Assert.Equal(1, context.Dialogs.ConfirmationRequests);
        Assert.Equal(originalPath, context.Session.ProjectPath);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
    }

    [Fact]
    public async Task ViewOnlyDirtyMarksTheTitleAndCloseCancellationThenSaveKeepsTheCorrectPersistenceState()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock);
        await context.InitializeAsync();
        var path = await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var cleanTitle = context.Session.ViewModel.Title;
        var row = GetRowId(snapshot, TimelineRowScope.SUBTITLE_TRACK);
        context.Session.SetTimelineAnimationRowCollapsed(row, true);

        Assert.Equal(cleanTitle + " •", context.Session.ViewModel.Title);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        context.Dialogs.UnsavedChoice = 0;

        Assert.False(await context.Session.RequestCloseAsync());

        Assert.Equal(1, context.Dialogs.ConfirmationRequests);
        Assert.False(context.Session.IsClosing);
        Assert.Equal(2, clock.ActiveTimerCount);
        Assert.Equal(cleanTitle + " •", context.Session.ViewModel.Title);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.Empty((await ProjectStore.LoadAsync(path)).TimelineViewState.CollapsedAnimationRows);
        context.Dialogs.UnsavedChoice = 1;
        var beforeDisposeCalled = false;

        var closed = await context.Session.RequestCloseAsync(() =>
        {
            beforeDisposeCalled = true;
            Assert.False(context.Session.HasUnsavedChanges);
            Assert.Equal(cleanTitle, context.Session.ViewModel.Title);
            return Task.CompletedTask;
        });

        Assert.True(closed);
        Assert.True(beforeDisposeCalled);
        Assert.Equal(2, context.Dialogs.ConfirmationRequests);
        Assert.True(context.Session.IsClosing);
        Assert.Equal(0, clock.ActiveTimerCount);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Equal(cleanTitle, context.Session.ViewModel.Title);
        Assert.Same(snapshot, context.Editor.Snapshot);
        Assert.Equal(row, Assert.Single((await ProjectStore.LoadAsync(path)).TimelineViewState.CollapsedAnimationRows));
        await context.Session.DisposeAsync();
        Assert.Equal(0, clock.ActiveTimerCount);
    }

    [Fact]
    public async Task OlderAutoSaveKeepsNewContentAndViewDirtyAndContentUndoReturnsToItsSavePointWithoutChangingView()
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var subtitleRow = GetRowId(snapshot, TimelineRowScope.SUBTITLE_TRACK);
        var sceneRow = GetRowId(snapshot, TimelineRowScope.SCENE_LAYER);
        context.Session.SetTimelineAnimationRowCollapsed(subtitleRow, true);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.Editor.Apply("Resize during timeline state save", document => document with { Width = 1280 });
            var newerSnapshot = context.Editor.Snapshot;
            context.Session.SetTimelineAnimationRowCollapsed(sceneRow, true);
            release.TrySetResult();
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            var saved = Assert.Single(storage.SavedSnapshots);
            Assert.Equal(snapshot.Width, saved.Width);
            Assert.Equal(subtitleRow, Assert.Single(saved.TimelineViewState.CollapsedAnimationRows));
            Assert.Same(newerSnapshot, context.Editor.Snapshot);
            Assert.True(context.Editor.HasUnsavedChanges);
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.Equal(new[] { subtitleRow, sceneRow }, context.Session.TimelineViewState.CollapsedAnimationRows);
            Assert.Equal("Resize during timeline state save", context.Editor.UndoLabel);
            Assert.True(context.Editor.Undo());
            await context.Session.WaitForProjectIdleAsync();

            Assert.Same(snapshot, context.Editor.Snapshot);
            Assert.False(context.Editor.HasUnsavedChanges);
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.Equal(new[] { subtitleRow, sceneRow }, context.Session.TimelineViewState.CollapsedAnimationRows);
            Assert.True(context.Editor.CanRedo);
            storage.SaveWork = null;
            clock.Advance(TimeSpan.FromMinutes(2));
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(2, storage.SavedSnapshots.Count);
            Assert.Equal(snapshot.Width, storage.SavedSnapshots[1].Width);
            Assert.Equal(new[] { subtitleRow, sceneRow }, storage.SavedSnapshots[1].TimelineViewState.CollapsedAnimationRows);
            Assert.Same(snapshot, context.Editor.Snapshot);
            Assert.False(context.Session.HasUnsavedChanges);
            Assert.True(context.Editor.Redo());
            await context.Session.WaitForProjectIdleAsync();
            Assert.Same(newerSnapshot, context.Editor.Snapshot);
            Assert.True(context.Editor.HasUnsavedChanges);
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.Equal(new[] { subtitleRow, sceneRow }, context.Session.TimelineViewState.CollapsedAnimationRows);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static ProjectDocument CreateDocument()
    {
        var cue = new SubtitleLine { Start = new(1), End = new(5), Text = "Timeline view state" };
        return new()
        {
            Subtitles = [cue],
            Layers =
            [
                new()
                {
                    Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
                    Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.25)])]
                },
                new()
                {
                    Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20), End = new(5),
                    Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.75)])]
                }
            ]
        };
    }

    private static TimelineAnimationRowId GetRowId(ProjectDocument document, TimelineRowScope scope)
    {
        var ownerId = scope == TimelineRowScope.SUBTITLE_TRACK
            ? Assert.Single(document.SubtitleTracks).Id
            : Assert.Single(document.Layers, layer => layer.Kind == LayerKind.SHAPE).Id;
        return new(scope, ownerId, AnimationProperty.OPACITY);
    }

    private static async Task<string> OpenFixtureAsync(WorkspaceSessionTestContext context, ProjectDocument document)
    {
        var path = Path.Combine(context.DirectoryPath, "timeline.aeginext");
        await ProjectStore.SaveAsync(document, path);
        var result = await context.Session.OpenProjectAsync(path);
        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        await context.Session.WaitForProjectIdleAsync();
        Assert.False(context.Session.HasUnsavedChanges);
        return path;
    }
}
