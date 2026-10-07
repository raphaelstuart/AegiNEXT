namespace AegiNext.Desktop.Settings;

/// <summary>请求删除的个人压制预设稳定标识。</summary>
public sealed class SettingsExportPresetDeleteEventArgs(Guid id) : EventArgs
{
    public Guid Id { get; } = id;
}
