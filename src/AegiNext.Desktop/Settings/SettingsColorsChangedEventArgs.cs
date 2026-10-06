namespace AegiNext.Desktop.Settings;

/// <summary>已提交的个人界面主色、音频图和时间轴配色，不包含其他页面草稿。</summary>
public sealed class SettingsColorsChangedEventArgs(string accentColor, AudioGraphPalette audioGraph, TimelineClipPalette timelineClips) : EventArgs
{
    public string AccentColor { get; } = accentColor;
    public AudioGraphPalette AudioGraph { get; } = audioGraph;
    public TimelineClipPalette TimelineClips { get; } = timelineClips;
}
