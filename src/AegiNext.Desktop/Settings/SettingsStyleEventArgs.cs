using AegiNext.Core.Presets;

namespace AegiNext.Desktop.Settings;

/// <summary>请求保存或应用的独立字幕样式预设。</summary>
public sealed class SettingsStyleEventArgs(SubtitleStylePreset preset) : EventArgs
{
    public SubtitleStylePreset Preset { get; } = preset;
}
