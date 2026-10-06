namespace AegiNext.Media.Audio;

/// <summary>显式选择 SDL3 输出；播放时钟标记为缓冲估计。</summary>
public sealed class SdlAudioOutput : NativeAudioOutput
{
    /// <summary>创建初始暂停的 SDL 默认输出设备。</summary>
    public SdlAudioOutput() : base(false)
    {
    }
}
