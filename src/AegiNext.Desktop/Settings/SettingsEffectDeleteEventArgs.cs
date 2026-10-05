namespace AegiNext.Desktop.Settings;

/// <summary>按稳定身份请求删除个人特效模板。</summary>
public sealed class SettingsEffectDeleteEventArgs(Guid id) : EventArgs
{
    public Guid Id { get; } = id;
}
