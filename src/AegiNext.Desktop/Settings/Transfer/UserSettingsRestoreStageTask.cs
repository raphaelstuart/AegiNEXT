using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class UserSettingsRestoreStageTask(DesktopApplicationContext owner, UserSettingsBundle snapshot) : AegiTask
{
    public override string Name => "Tasks.SettingsRestoreStage";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. owner.SettingsResources, AegiTaskResource.DeferredStoragePath(Path.Combine(owner.PreferencesStore.DirectoryPath,
            ".settings-restore", "pending.aegisettings"))];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        return Task.Run(() => owner.SettingsRestore.StageAsync(snapshot, () => context.EnterCommit(), context.CancellationToken),
            context.CancellationToken);
    }
}
