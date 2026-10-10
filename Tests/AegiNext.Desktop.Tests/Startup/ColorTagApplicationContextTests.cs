using AegiNext.Application.ColorTags;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Startup;

/// <summary>验证个人标记库的成功发布、共享资源和应用生命周期。</summary>
[Collection("Workspace session")]
public sealed class ColorTagApplicationContextTests
{
    /// <summary>落盘提交失败不发布新快照，后续操作可以恢复并使用共享设置资源。</summary>
    [Fact]
    public async Task FailedCommitDoesNotPublishAndOperationsShareTheSettingsResource()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var original = context.ColorTagLibrary.Snapshot;
        var changes = 0;
        context.ColorTagsChanged += (_, _) => changes++;
        var failure = new IOException("Rejected tag commit.");
        var replacement = new SubtitleColorTagLibraryDocument
        {
            Tags = [new SubtitleColorTag { Name = "User", ColorHex = "#123456" }]
        };

        await Assert.ThrowsAsync<IOException>(() => context.RunColorTagOperationAsync(() =>
            context.ColorTagLibrary.ReplaceAsync(replacement, () => throw failure)));

        Assert.Same(original, context.ColorTagLibrary.Snapshot);
        Assert.Same(failure, context.LastError);
        Assert.False(context.ColorTagsBusy);
        Assert.Equal(0, changes);
        Assert.Contains(context.GetLibraryResource(PersonalLibraryKind.COLOR_TAG), context.SettingsResources);
        await context.RunColorTagOperationAsync(() => context.ColorTagLibrary.ReplaceAsync(replacement));
        Assert.Equal(replacement.Tags[0], Assert.Single(context.ColorTagLibrary.Snapshot.Tags));
        Assert.Equal(1, changes);
    }

    /// <summary>损坏文件保留原字节并阻止设置导出，成功替换后清除初始化错误。</summary>
    [Fact]
    public async Task CorruptTagLibraryIsPreservedAndBlocksSettingsExportUntilValidPublication()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "subtitle-color-tags.json");
        await File.WriteAllTextAsync(path, "{broken");
        await using var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;

        Assert.NotNull(context.SettingsLoadError);
        Assert.Equal("{broken", await File.ReadAllTextAsync(path));
        Assert.Empty(context.ColorTagLibrary.Snapshot.Tags);
        await context.RunColorTagOperationAsync(() => context.ColorTagLibrary.ReplaceAsync(new()));

        Assert.Null(context.SettingsLoadError);
    }

    /// <summary>应用关闭排空已开始的标记操作，再释放个人库并拒绝新工作。</summary>
    [Fact]
    public async Task DisposalDrainsTagOperationsAndDisposesTheLibrary()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var context = new DesktopApplicationContext(new(directory.Path));
        await context.Initialization;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var operation = context.RunColorTagOperationAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task;
            await context.ColorTagLibrary.ReplaceAsync(new());
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(context.ColorTagsBusy);
            var disposal = context.DisposeAsync().AsTask();
            Assert.False(disposal.IsCompleted);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => context.RunColorTagOperationAsync(() => Task.CompletedTask));
            release.TrySetResult();
            await Task.WhenAll(operation, disposal).WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => context.ColorTagLibrary.LoadAsync());
        }
        finally
        {
            release.TrySetResult();
            await operation;
            await context.DisposeAsync();
        }
    }
}
