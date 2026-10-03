using AegiNext.Core.Timing;

namespace AegiNext.Media.Audio;

/// <summary>
/// 保留原始媒体时间轴的独立交错 float32 样本块。
/// </summary>
public sealed class AudioSampleBlock
{
    /// <summary>
    /// 复制并接管一个完整 PCM 块，不与解码器复用的缓存共享所有权。
    /// </summary>
    public AudioSampleBlock(AudioSampleFormat format, MediaTime start, ReadOnlySpan<float> samples)
    {
        ArgumentNullException.ThrowIfNull(format);
        if (samples.Length == 0 || samples.Length % format.Channels != 0 || samples.Length > 524288)
        {
            throw new ArgumentException("PCM 块必须包含完整声道帧且不超过容量限制。", nameof(samples));
        }

        Format = format;
        Start = start;
        Samples = samples.ToArray();
    }

    public AudioSampleFormat Format { get; }
    public MediaTime Start { get; }
    public ReadOnlyMemory<float> Samples { get; }
    public int FrameCount => Samples.Length / Format.Channels;
}
