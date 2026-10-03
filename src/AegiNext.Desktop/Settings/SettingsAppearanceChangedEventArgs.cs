namespace AegiNext.Desktop.Settings;

/// <summary>用户选择的即时外观与语言设置。</summary>
public sealed class SettingsAppearanceChangedEventArgs(WorkbenchTheme theme, string language, string accentColor, bool windowMenuOnMac = false) : EventArgs
{
    public WorkbenchTheme Theme { get; } = theme;
    public string Language { get; } = language;
    public string AccentColor { get; } = accentColor;
    public bool WindowMenuOnMac { get; } = windowMenuOnMac;
}
