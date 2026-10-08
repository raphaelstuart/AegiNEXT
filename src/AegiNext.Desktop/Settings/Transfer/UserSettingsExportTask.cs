using AegiNext.Application.Tasks;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class UserSettingsExportTask(DesktopApplicationContext owner, UserSettingsBundle snapshot,
    string path) : AegiTask
{
    public override string Name => "Tasks.SettingsExport";

    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. owner.SettingsResources, AegiTaskResource.DeferredStoragePath(path)];

    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        if (owner.SettingsLoadError is { } error)
        {
            throw new InvalidOperationException(Localization.Get("Settings.TransferExportUnavailable"), error);
        }

        context.ReportProgress(new(Name));
        var token = context.CancellationToken;
        var bytes = await Task.Run(() => UserSettingsBundleStore.Serialize(snapshot, token), token);
        token.ThrowIfCancellationRequested();
        await UserSettingsTransferFiles.WriteAtomicAsync(Path.GetFullPath(path), bytes, () => context.EnterCommit(), token);
    }
}
