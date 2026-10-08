using AegiNext.Application.Tasks;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class UserSettingsLayoutCaptureTask(DesktopApplicationContext owner) : AegiTask<WorkspaceLayoutFile>
{
    private readonly string directory = owner.PreferencesStore.DirectoryPath;

    public override string Name => "Tasks.SettingsCaptureLayout";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [AegiTaskResource.StoragePath(Path.Combine(directory, "layouts.json"))];

    protected override Task<WorkspaceLayoutFile> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        return Task.Run(() =>
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            using var store = new WorkspaceLayoutStore(directory);
            var snapshot = store.LoadStrict();
            context.CancellationToken.ThrowIfCancellationRequested();
            return snapshot;
        }, context.CancellationToken);
    }
}
