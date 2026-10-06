namespace AegiNext.Media.Audio;

/// <summary>
/// 有界 48kHz 立体声 PCM 输出设备；队列统计使用源格式样本帧。
/// </summary>
public interface IAudioOutput : IDisposable
{
    int QueuedFrames { get; }
    int LatencyFrames { get; }

    /// <summary>读取播放位置与设备身份；旧输出只提供明确标记的队列估计。</summary>
    AudioOutputClockSnapshot ReadClock()
    {
        return new(0, 0, 1, "unknown", "estimated", 0, AudioClockQuality.ESTIMATED, QueuedFrames);
    }

    /// <summary>写入交错立体声样本，队列总量不得超过 12000 帧。</summary>
    void Write(ReadOnlySpan<float> samples);
    /// <summary>暂停或恢复设备消费。</summary>
    void SetPaused(bool paused);
    /// <summary>清空设备尚未消费的 PCM。</summary>
    void Clear();
    /// <summary>设置零到一之间的输出增益。</summary>
    void SetGain(float gain);
}
