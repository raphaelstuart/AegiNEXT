namespace AegiNext.Desktop.Settings;

/// <summary>请求从用户样式库删除的预设标识。</summary>
public sealed class SettingsStyleDeleteEventArgs(Guid id) : EventArgs
{
    public Guid Id { get; } = id;
}
