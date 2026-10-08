using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class ProjectOpenWorkflowTests
{
    [Fact]
    public async Task CancelledTokenAndDeclinedUnsavedChangesPreserveTheProjectAndRecentHistory()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);
        var path = Path.Combine(context.DirectoryPath, "requested.aeginext");
        await ProjectStore.SaveAsync(new() { Name = "Requested" }, path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var original = context.Editor.Snapshot;

        var cancelled = await workflow.OpenProjectAsync(path, cancellation.Token);

        Assert.Equal(ProjectOpenStatus.CANCELLED, cancelled.Status);
        Assert.Null(cancelled.Error);
        Assert.Empty(cancelled.Diagnostics);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
        context.Editor.Apply("Unsaved", document => document with { Width = 1280 });
        original = context.Editor.Snapshot;
        context.Dialogs.UnsavedChoice = 0;

        var declined = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.CANCELLED, declined.Status);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(context.Editor.HasUnsavedChanges);
        Assert.Equal(1, context.Dialogs.ConfirmationRequests);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
    }

    [Fact]
    public async Task InvalidProjectReturnsFailedWithoutReplacingTheProjectOrRecordingThePath()
    {
        await using var context = new WorkspaceSessionTestContext(new() { Name = "Existing" });
        await context.InitializeAsync();
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);
        var path = Path.Combine(context.DirectoryPath, "broken.aeginext");
        await File.WriteAllTextAsync(path, "invalid project JSON");
        var original = context.Editor.Snapshot;

        var result = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.FAILED, result.Status);
        Assert.IsType<InvalidDataException>(result.Error);
        Assert.Empty(result.Diagnostics);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.Null(context.Session.ProjectPath);
        Assert.False(context.Session.IsProjectBusy);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
    }

    [Fact]
    public async Task SuccessfulDirectAndPickerOpenAndSaveShareTheRecentProjectHistory()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);
        var path = Path.Combine(context.DirectoryPath, "first.aeginext");
        await ProjectStore.SaveAsync(new() { Name = "First" }, path);

        var result = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        Assert.Null(result.Error);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(path, context.Session.ProjectPath);
        Assert.Equal("First", context.Editor.Snapshot.Name);
        Assert.Equal(path, Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries).Path);
        context.Dialogs.OpenPath = path;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.Null(context.Session.LastError);
        Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries);

        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "saved-as.aeginext");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT_AS);
        Assert.Null(context.Session.LastError);
        Assert.Equal(context.Dialogs.SavePath, context.Session.ApplicationContext.RecentProjects.Entries[0].Path);
        Assert.Equal(2, context.Session.ApplicationContext.RecentProjects.Entries.Count);
    }

    [Fact]
    public async Task PickerCancellationAndFailedOpenKeepHistoryEmptyAndSurfaceTheFailure()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);

        Assert.Null(context.Session.LastError);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
        context.Dialogs.OpenPath = Path.Combine(context.DirectoryPath, "missing.aeginext");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_PROJECT);
        Assert.IsType<FileNotFoundException>(context.Session.LastError);
        Assert.Empty(context.Session.ApplicationContext.RecentProjects.Entries);
    }

    [Fact]
    public async Task DecliningUnavailableMediaCancelsBeforeCommitAndRestoresThePreviousProjectPreview()
    {
        using var original = new TemporaryWorkbenchDirectory();
        using var missing = new TemporaryWorkbenchDirectory();
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;
        var source = Path.Combine(original.Path, "video.mkv");
        await File.WriteAllTextAsync(source, "media fixture");
        await context.Session.OpenMediaAsync(source, true);
        context.Dialogs.SavePath = Path.Combine(original.Path, "existing.aeginext");
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.SAVE_PROJECT);
        await context.Session.Controller.SeekAsync(new(1, 10));
        var previousDocument = context.Session.Editor.Snapshot;
        var previousPosition = context.Session.Controller.Snapshot.Position;
        var previousHistory = context.Session.ApplicationContext.RecentProjects.Entries.ToArray();
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        var path = Path.Combine(missing.Path, "requested.aeginext");
        await ProjectStore.SaveAsync(new()
        {
            Width = 1, Height = 1, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        }, path);
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);

        var result = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.CANCELLED, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(1, context.Dialogs.UnavailableMediaRequests);
        Assert.Same(previousDocument, context.Session.Editor.Snapshot);
        Assert.Equal(original.Path, context.Session.ProjectDirectory);
        Assert.Equal(source, context.Session.Controller.Snapshot.FilePath);
        Assert.Equal(previousPosition, context.Session.Controller.Snapshot.Position);
        Assert.Equal(previousHistory, context.Session.ApplicationContext.RecentProjects.Entries);
    }

    [Fact]
    public async Task HistoryWriteFailureKeepsTheOpenedProjectCommittedAndItsRecordInMemory()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var historyPath = Path.Combine(context.DirectoryPath, "recent-projects.json");
        Directory.CreateDirectory(historyPath);
        var path = Path.Combine(context.DirectoryPath, "opened.aeginext");
        await ProjectStore.SaveAsync(new() { Name = "Opened" }, path);
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);

        var result = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        Assert.Null(result.Error);
        Assert.Equal(path, context.Session.ProjectPath);
        Assert.Equal("Opened", context.Editor.Snapshot.Name);
        Assert.Equal(path, Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries).Path);
        Assert.NotNull(context.Session.ApplicationContext.RecentProjects.LastError);
    }

    [Fact]
    public async Task CallerWaitCancellationAfterCommitDoesNotUndoTheOpenedProject()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var path = Path.Combine(context.DirectoryPath, "committed.aeginext");
        await ProjectStore.SaveAsync(new() { Name = "Committed" }, path);
        using var cancellation = new CancellationTokenSource();
        context.Session.ApplicationContext.RecentProjects.Changed += (_, _) => cancellation.Cancel();
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);

        var result = await workflow.OpenProjectAsync(path, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(ProjectOpenStatus.CANCELLED, result.Status);
        Assert.Null(result.Error);
        await context.Session.WaitForProjectIdleAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(path, context.Session.ProjectPath);
        Assert.Equal("Committed", context.Editor.Snapshot.Name);
        Assert.Equal(path, Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries).Path);
    }

    [Fact]
    public async Task SeekFailureAfterCommitReturnsOpenedWithADiagnosticAndRecordsTheProject()
    {
        await using var context = new PostCommitProjectOpenTestContext();
        await context.Session.Styles.Completion;
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "fixture.media");
        await File.WriteAllTextAsync(Path.Combine(context.DirectoryPath, asset.RelativePath), "media fixture");
        var path = Path.Combine(context.DirectoryPath, "committed.aeginext");
        await ProjectStore.SaveAsync(new()
        {
            Name = "Committed", Width = 1, Height = 1, Assets = [asset],
            Media = new(asset.Id, 0, null, MediaTime.Zero)
        }, path);
        var workflow = new ProjectWorkflowCoordinator(context.Session, context.Dialogs);

        var result = await workflow.OpenProjectAsync(path);

        Assert.Equal(ProjectOpenStatus.OPENED, result.Status);
        Assert.Null(result.Error);
        Assert.Contains(result.Diagnostics, diagnostic => ReferenceEquals(context.Source.Failure, diagnostic));
        Assert.Equal(path, context.Session.ProjectPath);
        Assert.Equal("Committed", context.Session.Editor.Snapshot.Name);
        Assert.Equal(path, Assert.Single(context.Session.ApplicationContext.RecentProjects.Entries).Path);
        Assert.False(context.Session.IsProjectBusy);
        await context.Session.DisposeAsync();
        Assert.Equal(1, context.Source.DisposeCount);
    }
}
