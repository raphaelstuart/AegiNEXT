namespace AegiNext.Media.Audio;

/// <summary>macOS 使用 CoreAudio，Windows 使用 WASAPI；其他平台显式提供 SDL 估计时钟。</summary>
public sealed class SystemAudioOutput : NativeAudioOutput
{
    /// <summary>创建默认系统输出及其同一输出流的播放时钟，初始暂停。</summary>
    public SystemAudioOutput() : base(true)
    {
    }
}
