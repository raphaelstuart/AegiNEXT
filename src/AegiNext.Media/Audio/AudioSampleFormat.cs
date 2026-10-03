namespace AegiNext.Media.Audio;

/// <summary>
/// 交错 float32 PCM 的采样率及声道数。
/// </summary>
public sealed record AudioSampleFormat
{
    /// <summary>
    /// 创建有界的单声道或立体声输出格式。
    /// </summary>
    public AudioSampleFormat(int sampleRate = 48000, int channels = 2)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 8000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sampleRate, 192000);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(channels, 2);
        SampleRate = sampleRate;
        Channels = channels;
    }

    public int SampleRate { get; }
    public int Channels { get; }
}
