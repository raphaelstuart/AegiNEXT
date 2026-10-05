namespace AegiNext.Desktop.Settings;

/// <summary>已提交的个人界面主色与音频图配色，不包含其他页面草稿。</summary>
public sealed class SettingsColorsChangedEventArgs(string accentColor, AudioGraphPalette audioGraph) : EventArgs
{
    public string AccentColor { get; } = accentColor;
    public AudioGraphPalette AudioGraph { get; } = audioGraph;
}
