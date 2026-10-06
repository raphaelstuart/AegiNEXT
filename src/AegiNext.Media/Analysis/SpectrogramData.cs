using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

/// <summary>
/// 有界语谱图缓存；频率行从低到高，列覆盖工程时间区间，FFT 中心位于每列中心。
/// </summary>
public sealed class SpectrogramData
{
    /// <summary>
    /// 创建独立缓存，亮度按 frequency × width + column 排列。
    /// </summary>
    public SpectrogramData(int width, int height, MediaTime duration, byte[] levels, float[] waveform)
        : this(width, height, MediaTime.Zero, GetColumnDuration(width, duration), levels, waveform)
    {
    }

    /// <summary>建立局部频谱；每列以区间中心的 FFT 代表该列，粗列仅为概览采样，步长独立于波形分辨率。</summary>
    public SpectrogramData(int width, int height, MediaTime start, MediaTime columnDuration, byte[] levels)
        : this(width, height, start, columnDuration, levels, [])
    {
    }

    private SpectrogramData(int width, int height, MediaTime start, MediaTime columnDuration, byte[] levels, float[] waveform)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(waveform);
        if (width > 32768 || height > 512 || columnDuration <= MediaTime.Zero ||
            levels.Length != checked(width * height) || waveform.Length != 0 && waveform.Length != checked(width * 2))
        {
            throw new ArgumentException("语谱缓存尺寸、时间或数据长度无效。");
        }

        Width = width;
        Height = height;
        Start = start;
        ColumnDuration = columnDuration;
        Duration = columnDuration * width;
        Levels = levels.ToArray();
        Waveform = waveform.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public MediaTime Duration { get; }

    public MediaTime Start { get; }

    public MediaTime End => Start + Duration;

    public MediaTime ColumnDuration { get; }

    public ReadOnlyMemory<byte> Levels { get; }

    public ReadOnlyMemory<float> Waveform { get; }

    private static MediaTime GetColumnDuration(int width, MediaTime duration)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        return duration / width;
    }
}
