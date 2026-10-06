namespace AegiNext.Media.Audio;

/// <summary>一次原子读取的输出位置，播放帧从最近一次 Clear 开始，主机时间使用对应频率。</summary>
public sealed record AudioOutputClockSnapshot(
    long PlayedFrames,
    long HostTimestamp,
    long HostFrequency,
    string DeviceId,
    string Backend,
    long Epoch,
    AudioClockQuality Quality,
    int QueuedFrames,
    int SampleRate = 48000,
    int Channels = 2);
