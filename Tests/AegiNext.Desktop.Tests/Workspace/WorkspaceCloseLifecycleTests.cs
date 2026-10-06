using AegiNext.Core.Projects;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class WorkspaceCloseLifecycleTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task CancelledUnsavedPromptOrSaveDialogLeavesSessionAndPreviewAlive(int choice)
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        await context.Session.Controller.OpenAsync("controlled.mkv");
        context.Dialogs.UnsavedChoice = choice;
        context.Dialogs.SavePath = null;
        var beforeDisposeCalls = 0;

        Assert.False(await context.Session.RequestCloseAsync(() =>
        {
            beforeDisposeCalls++;
            return Task.CompletedTask;
        }));

        Assert.False(context.Session.IsClosing);
        Assert.Equal(0, beforeDisposeCalls);
        Assert.Equal(1, context.Dialogs.ConfirmationRequests);
        Assert.Equal(choice == 1 ? 1 : 0, context.Dialogs.SaveRequests);
        Assert.Equal(0, context.Source.DisposeCount);
        Assert.Equal(0, context.Converter.DisposeCount);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Session.Controller.Snapshot.State);
        Assert.True(context.Editor.HasUnsavedChanges);
        await context.Session.Controller.SeekAsync(new(1, 10));
        Assert.Null(context.Session.Controller.Snapshot.Error);
    }

    [Fact]
    public async Task FailedSavePreservesUnsavedSnapshotAndDoesNotRunDisposalBoundary()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        var original = context.Editor.Snapshot;
        await context.Session.Controller.OpenAsync("controlled.mkv");
        context.Dialogs.UnsavedChoice = 1;
        var blockedDestination = Path.Combine(context.DirectoryPath, "blocked.aeginext");
        Directory.CreateDirectory(blockedDestination);
        context.Dialogs.SavePath = blockedDestination;
        var beforeDisposeCalls = 0;

        await Assert.ThrowsAnyAsync<IOException>(() => context.Session.RequestCloseAsync(() =>
        {
            beforeDisposeCalls++;
            return Task.CompletedTask;
        }));

        Assert.Same(original, context.Editor.Snapshot);
        Assert.True(context.Editor.HasUnsavedChanges);
        Assert.False(context.Session.IsClosing);
        Assert.Equal(0, beforeDisposeCalls);
        Assert.Equal(0, context.Source.DisposeCount);
        Assert.Equal(0, context.Converter.DisposeCount);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Session.Controller.Snapshot.State);
        Assert.Empty(Directory.EnumerateFiles(context.DirectoryPath, "*.tmp"));
    }

    [Fact]
    public async Task SuccessfulCloseAwaitsOneBoundaryAndReleasesControllerResourcesOnce()
    {
        await using var context = new WorkspaceSessionTestContext(new ProjectDocument { Name = "Close" });
        await context.InitializeAsync();
        context.Editor.Apply("Unsaved name", document => document with { Name = "Pending" });
        await context.Session.Controller.OpenAsync("controlled.mkv");
        context.Dialogs.UnsavedChoice = 2;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var beforeDisposeCalls = 0;
        var close = context.Session.RequestCloseAsync(async () =>
        {
            beforeDisposeCalls++;
            entered.TrySetResult();
            await release.Task;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(close.IsCompleted);
            Assert.True(context.Session.IsClosing);
            Assert.Equal(0, context.Source.DisposeCount);
            Assert.Equal(0, context.Converter.DisposeCount);
            Assert.False(await context.Session.RequestCloseAsync());
            release.TrySetResult();
            Assert.True(await close.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, beforeDisposeCalls);
            Assert.Equal(1, context.Source.DisposeCount);
            Assert.Equal(1, context.Converter.DisposeCount);
            Assert.All(context.Source.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
            Assert.Equal(VideoPlaybackState.CLOSED, context.Session.Controller.Snapshot.State);
            Assert.False(Directory.Exists(context.Session.ScratchDirectory));
            await context.Session.DisposeAsync();
            Assert.Equal(1, context.Source.DisposeCount);
            Assert.Equal(1, context.Converter.DisposeCount);
            await Assert.ThrowsAsync<ObjectDisposedException>(() =>
                context.Session.Controller.OpenAsync("another.mkv"));
        }
        finally
        {
            release.TrySetResult();
            await close;
        }
    }
}
