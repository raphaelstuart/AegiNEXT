using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class UserSettingsRestoreCancelTask(DesktopApplicationContext owner) : AegiTask
{
    public override string Name => "Tasks.SettingsRestoreCancel";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. owner.SettingsResources, AegiTaskResource.StoragePath(Path.Combine(owner.PreferencesStore.DirectoryPath,
            ".settings-restore", "pending.aegisettings"))];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        return Task.Run(() => owner.SettingsRestore.CancelPendingAsync(() => context.EnterCommit(), context.CancellationToken),
            context.CancellationToken);
    }
}
