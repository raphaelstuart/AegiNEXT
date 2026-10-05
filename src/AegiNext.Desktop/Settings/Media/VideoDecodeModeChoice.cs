using AegiNext.Media.Decoding;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AegiNext.Desktop.Settings.Media;

/// <summary>解码选项的稳定身份及当前语言的显示名称。</summary>
public sealed class VideoDecodeModeChoice(VideoDecodeMode mode) : ObservableObject
{
    private string label = string.Empty;

    public VideoDecodeMode Mode { get; } = mode;
    public string Label => label;

    internal void UpdateLabel(string value)
    {
        SetProperty(ref label, value, nameof(Label));
    }
}
