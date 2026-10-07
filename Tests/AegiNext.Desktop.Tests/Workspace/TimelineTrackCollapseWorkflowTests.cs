using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class TimelineTrackCollapseWorkflowTests
{
    [Fact]
    public async Task OverallAndAnimationCollapseKeepEachOthersStateAndDoNotChangeContentHistory()
    {
        var document = CreateDocument();
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var track = document.SubtitleTracks[0].Id;
        var child = document.Layers[0].Children[0];
        var row = new TimelineAnimationRowId(TimelineRowScope.SCENE_LAYER, child.Id, AnimationProperty.OPACITY);

        context.Session.SetTimelineTrackCollapsed(track, true);
        context.Session.SetTimelineAnimationRowCollapsed(row, true);
        context.Session.SetTimelineTrackCollapsed(child.Id, true);

        Assert.Equal(new[] { track, child.Id }.Order(), context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.Same(document, context.Editor.Snapshot);
        Assert.True(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Editor.CanRedo);
        context.Session.SetTimelineTrackCollapsed(track, false);
        context.Session.SetTimelineTrackCollapsed(child.Id, false);
        context.Session.SetTimelineAnimationRowCollapsed(row, false);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(document, context.Editor.Snapshot);
    }

    [Fact]
    public async Task AllCommandsIncludeSoloHiddenTracksAndHiddenGroupChildrenInOneViewPublication()
    {
        var document = CreateDocument();
        var child = document.Layers[0].Children[0];
        var row = new TimelineAnimationRowId(TimelineRowScope.SCENE_LAYER, child.Id, AnimationProperty.OPACITY);
        document = document with { TimelineViewState = new() { CollapsedAnimationRows = [row] } };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        var publications = 0;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(model.TimelineViewState))
            {
                publications++;
            }
        };
        model.ToggleTrackSolo(document.SubtitleTracks[0].Id);

        model.CollapseAllTracksCommand.Execute(null);

        var expected = document.SubtitleTracks.Select(track => track.Id)
            .Concat(new[] { document.Layers[0].Id, child.Id }).Order().ToArray();
        Assert.Equal(expected, context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.Equal(1, publications);
        Assert.False(model.CollapseAllTracksCommand.CanExecute(null));
        Assert.True(model.ExpandAllTracksCommand.CanExecute(null));
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.Equal(document.SubtitleTracks[0].Id, model.SoloTrackId);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);

        model.ExpandAllTracksCommand.Execute(null);

        Assert.Empty(context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.Equal(row, Assert.Single(context.Session.TimelineViewState.CollapsedAnimationRows));
        Assert.Equal(2, publications);
        Assert.False(model.ExpandAllTracksCommand.CanExecute(null));
        Assert.True(model.CollapseAllTracksCommand.CanExecute(null));
        Assert.False(context.Session.HasUnsavedChanges);
    }

    [Fact]
    public async Task SaveAndSaveAsRestoreEachProjectsOverallCollapseWithoutChangingContentSnapshot()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var path = await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var track = snapshot.SubtitleTracks[0].Id;
        var child = snapshot.Layers[0].Children[0].Id;
        context.Session.SetTimelineTrackCollapsed(track, true);

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Equal(track, Assert.Single((await ProjectStore.LoadAsync(path)).TimelineViewState.CollapsedTrackIds));
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.Same(snapshot, context.Editor.Snapshot);
        context.Session.SetTimelineTrackCollapsed(child, true);
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "copy", "collapse.aeginext");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.Null(context.Session.LastError);
        Assert.Equal(new[] { track, child }.Order(),
            (await ProjectStore.LoadAsync(context.Dialogs.SavePath)).TimelineViewState.CollapsedTrackIds);
        Assert.False(context.Session.HasUnsavedChanges);

        Assert.Equal(ProjectOpenStatus.OPENED, (await context.Session.OpenProjectAsync(path)).Status);
        Assert.Equal(track, Assert.Single(context.Session.TimelineViewState.CollapsedTrackIds));
        Assert.Equal(ProjectOpenStatus.OPENED, (await context.Session.OpenProjectAsync(context.Dialogs.SavePath)).Status);
        Assert.Equal(new[] { track, child }.Order(), context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.Equal(context.Session.TimelineViewState, context.Session.ViewModel.Timeline.TimelineViewState);
        Assert.False(context.Editor.CanUndo);
        Assert.False(context.Session.HasUnsavedChanges);
    }

    [Fact]
    public async Task AutoSaveWritesOverallViewOnlyAndANewerCollapseRemainsDirtyUntilTheNextTick()
    {
        var clock = new ManualPlaybackTimeProvider();
        var storage = new ProjectPersistenceStorageStub();
        await using var context = new WorkspaceSessionTestContext(persistenceTimeProvider: clock, persistenceStorage: storage);
        await context.InitializeAsync();
        await OpenFixtureAsync(context, CreateDocument());
        var snapshot = context.Editor.Snapshot;
        var track = snapshot.SubtitleTracks[0].Id;
        var child = snapshot.Layers[0].Children[0].Id;
        context.Session.SetTimelineTrackCollapsed(track, true);
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
            context.Session.SetTimelineTrackCollapsed(child, true);
            release.TrySetResult();
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(track, Assert.Single(Assert.Single(storage.SavedSnapshots).TimelineViewState.CollapsedTrackIds));
            Assert.True(context.Session.HasUnsavedChanges);
            Assert.Same(snapshot, context.Editor.Snapshot);
            Assert.False(context.Editor.HasUnsavedChanges);
            Assert.False(context.Editor.CanUndo);
            storage.SaveWork = null;
            clock.Advance(TimeSpan.FromMinutes(2));
            await context.Session.Persistence.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, storage.SavedSnapshots.Count);
            Assert.Equal(new[] { track, child }.Order(), storage.SavedSnapshots[1].TimelineViewState.CollapsedTrackIds);
            Assert.False(context.Session.HasUnsavedChanges);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task DormantOverallIdentitySurvivesContentUndoAndInitializationNormalizesStableOrdering()
    {
        var document = CreateDocument();
        var ids = document.SubtitleTracks.Select(track => track.Id).Append(document.Layers[0].Id)
            .OrderDescending().ToArray();
        document = document with { TimelineViewState = new() { CollapsedTrackIds = [.. ids] } };
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        Assert.Equal(ids.Order(), context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.False(context.Session.HasUnsavedChanges);
        var track = document.SubtitleTracks[1].Id;
        context.Editor.RemoveSubtitleTrack(track);
        await context.Session.WaitForProjectIdleAsync();
        Assert.Contains(track, context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.True(context.Editor.Undo());
        await context.Session.WaitForProjectIdleAsync();
        Assert.Contains(track, context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.Same(document, context.Editor.Snapshot);
        Assert.True(context.Editor.CanRedo);
        context.Session.SetTimelineTrackCollapsed(track, false);
        Assert.True(context.Editor.CanRedo);
        context.Session.SetTimelineTrackCollapsed(track, true);
        Assert.False(context.Session.HasUnsavedChanges);
    }

    [Fact]
    public async Task EmptyDocumentHasNoBulkTargetsAndUnknownTrackDoesNotCreateViewDirty()
    {
        await using var context = new WorkspaceSessionTestContext(new() { SubtitleTracks = [] });
        await context.InitializeAsync();
        var model = context.Session.ViewModel.Timeline;
        Assert.False(model.CollapseAllTracksCommand.CanExecute(null));
        Assert.False(model.ExpandAllTracksCommand.CanExecute(null));

        context.Session.SetTimelineTrackCollapsed(Guid.NewGuid(), true);
        context.Session.SetAllTimelineTracksCollapsed(true);

        Assert.Empty(context.Session.TimelineViewState.CollapsedTrackIds);
        Assert.False(context.Session.HasUnsavedChanges);
        Assert.False(context.Editor.CanUndo);
    }

    private static ProjectDocument CreateDocument()
    {
        var child = new ProjectLayer
        {
            Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 40, 20),
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1), 0.5)])]
        };
        return new()
        {
            SubtitleTracks = [SubtitleTrack.Default, new() { Name = "Other" }],
            Layers = [new() { Name = "Group", Children = [child] }]
        };
    }

    private static async Task<string> OpenFixtureAsync(WorkspaceSessionTestContext context, ProjectDocument document)
    {
        var path = Path.Combine(context.DirectoryPath, "overall.aeginext");
        await ProjectStore.SaveAsync(document, path);
        Assert.Equal(ProjectOpenStatus.OPENED, (await context.Session.OpenProjectAsync(path)).Status);
        await context.Session.WaitForProjectIdleAsync();
        Assert.False(context.Session.HasUnsavedChanges);
        return path;
    }
}
