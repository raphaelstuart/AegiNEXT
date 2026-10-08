using AegiNext.Application.Tasks;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings.Transfer;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Tests.Settings.Transfer;

[Collection("Workspace session")]
public sealed class SettingsTransferTaskTests
{
    [Fact]
    public async Task ExportAndImportUseCapturedSettingsAndPublishTaskHistory()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = new DesktopApplicationContext(new(settingsDirectory.Path), new());
        await owner.Initialization;
        var snapshot = UserSettingsTransferTestData.CreateBundle();
        var outputPath = Path.Combine(outputDirectory.Path, "settings.aegisettings");
        var export = owner.Tasks.Submit(new UserSettingsExportTask(owner, snapshot, outputPath));
        await export.Completion;
        var import = owner.Tasks.Submit(new UserSettingsImportTask(outputPath));
        var result = await import.Completion;

        UserSettingsTransferTestData.AssertBundleEqual(snapshot, result);
        Assert.Equal(AegiTaskState.Succeeded, export.Snapshot.State);
        Assert.Equal(AegiTaskState.Succeeded, import.Snapshot.State);
        Assert.Contains(owner.Tasks.GetSnapshots(), task => task.Id == export.Id && task.Name == "Tasks.SettingsExport");
        Assert.Contains(owner.Tasks.GetSnapshots(), task => task.Id == import.Id && task.Name == "Tasks.SettingsImport");
    }

    [Fact]
    public async Task CapturedStoredLayoutDoesNotChangeWhenTheLayoutFileIsUpdatedBeforeExport()
    {
        using var settingsDirectory = new TemporaryWorkbenchDirectory();
        using var outputDirectory = new TemporaryWorkbenchDirectory();
        await using var owner = new DesktopApplicationContext(new(settingsDirectory.Path), new());
        await owner.Initialization;
        using var layouts = new WorkspaceLayoutStore(settingsDirectory.Path);
        var snapshot = UserSettingsTransferTestData.CreateBundle();
        await layouts.SaveAsync(snapshot.Layouts);
        var captureTask = new UserSettingsLayoutCaptureTask(owner);
        Assert.Contains(layouts.Resource, captureTask.Resources);
        var captured = await owner.Tasks.Submit(captureTask).Completion;
        var changed = snapshot.Layouts with
        {
            Presets = [snapshot.Layouts.Presets.Single() with { Name = "Later layout" }]
        };
        await layouts.SaveAsync(changed);
        var frozen = snapshot with { Layouts = captured };
        var outputPath = Path.Combine(outputDirectory.Path, "settings.aegisettings");

        await owner.Tasks.Submit(new UserSettingsExportTask(owner, frozen, outputPath)).Completion;

        var exported = await UserSettingsBundleStore.LoadAsync(outputPath);
        UserSettingsTransferTestData.AssertBundleEqual(snapshot, exported);
        Assert.Equal("Later layout", layouts.LoadStrict().Presets.Single().Name);
    }

    [Fact]
    public async Task StageAndDiscardRunThroughSharedTaskServiceWithoutApplyingLivePreferences()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var owner = new DesktopApplicationContext(new(directory.Path), new() { AccentColor = "#123456" });
        await owner.Initialization;
        var snapshot = UserSettingsTransferTestData.CreateBundle();
        var stage = owner.Tasks.Submit(new UserSettingsRestoreStageTask(owner, snapshot));
        await stage.Completion;

        Assert.True(owner.SettingsRestore.HasPending);
        Assert.Equal("#123456", owner.Preferences.AccentColor);
        var pending = await UserSettingsBundleStore.LoadAsync(Path.Combine(directory.Path, ".settings-restore", "pending.aegisettings"));
        UserSettingsTransferTestData.AssertBundleEqual(snapshot, pending);
        var discard = owner.Tasks.Submit(new UserSettingsRestoreCancelTask(owner));
        await discard.Completion;

        Assert.False(owner.SettingsRestore.HasPending);
        Assert.Equal(AegiTaskState.Succeeded, stage.Snapshot.State);
        Assert.Equal(AegiTaskState.Succeeded, discard.Snapshot.State);
    }

    [Fact]
    public async Task AtomicWriteEntersCommitAfterPreparationAndRejectedCommitKeepsOriginalFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var directoryPath = directory.Path;
        var path = Path.Combine(directoryPath, "settings.aegisettings");
        await File.WriteAllTextAsync(path, "original");
        var invoked = false;
        byte[] replacementBytes = [1, 2, 3];

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => UserSettingsTransferFiles.WriteAtomicAsync(path,
            replacementBytes, () =>
            {
                invoked = true;
                Assert.Equal("original", File.ReadAllText(path));
                Assert.Single(Directory.EnumerateFiles(directoryPath, ".*.tmp"));
                throw new OperationCanceledException();
            }, CancellationToken.None));

        Assert.True(invoked);
        Assert.Equal("original", await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.EnumerateFiles(directory.Path, ".*.tmp"));
    }

    [Fact]
    public async Task RejectedRestoreCommitPreservesExistingPendingBundleAndDiscardChecksBeforeDeletion()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var restore = new UserSettingsRestoreService(directory.Path);
        var original = new UserSettingsBundle();
        await restore.StageAsync(original);
        var replacement = UserSettingsTransferTestData.CreateBundle();
        var path = Path.Combine(directory.Path, ".settings-restore", "pending.aegisettings");
        var originalBytes = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore.StageAsync(replacement,
            () => throw new OperationCanceledException()));
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => restore.CancelPendingAsync(
            () => throw new OperationCanceledException()));
        Assert.True(restore.HasPending);
        Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, ".*.tmp"));
    }

    [Fact]
    public void BundleSerializationAndDecompressionHonorCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var bundle = UserSettingsTransferTestData.CreateBundle();
        Assert.ThrowsAny<OperationCanceledException>(() => UserSettingsBundleStore.Serialize(bundle, cancellation.Token));
        var bytes = UserSettingsBundleStore.Serialize(bundle);
        Assert.ThrowsAny<OperationCanceledException>(() => UserSettingsBundleStore.Deserialize(bytes, cancellation.Token));
    }
}
