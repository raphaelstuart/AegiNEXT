using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Settings.Transfer;

internal sealed class UserSettingsImportTask(string path) : AegiTask<UserSettingsBundle>
{
    public override string Name => "Tasks.SettingsImport";

    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.StoragePath(path)];

    protected override async Task<UserSettingsBundle> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        context.ReportProgress(new(Name));
        var token = context.CancellationToken;
        var bytes = await UserSettingsTransferFiles.ReadAsync(path, UserSettingsBundleStore.MAXIMUM_FILE_BYTES, token);
        return await Task.Run(() => UserSettingsBundleStore.Deserialize(bytes, token), token);
    }
}
