using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Settings;

/// <summary>请求切换当前预览会话的解码方式。</summary>
public sealed class SettingsPreviewDecodeModeChangedEventArgs(VideoDecodeMode mode) : EventArgs
{
    public VideoDecodeMode Mode { get; } = mode;
}
