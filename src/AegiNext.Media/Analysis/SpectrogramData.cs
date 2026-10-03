using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

/// <summary>
/// 有界语谱图缓存；频率行从低到高，列对应工程相对时间，波形每列保存最小与最大值。
/// </summary>
public sealed class SpectrogramData
{
    /// <summary>
    /// 创建独立缓存，亮度按 frequency × width + column 排列。
    /// </summary>
    public SpectrogramData(int width, int height, MediaTime duration, byte[] levels, float[] waveform)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentNullException.ThrowIfNull(waveform);
        if (width > 8192 || height > 512 || duration <= MediaTime.Zero ||
            levels.Length != checked(width * height) || waveform.Length != checked(width * 2))
        {
            throw new ArgumentException("语谱缓存尺寸、时间或数据长度无效。");
        }

        Width = width;
        Height = height;
        Duration = duration;
        Levels = levels.ToArray();
        Waveform = waveform.ToArray();
    }

    public int Width { get; }

    public int Height { get; }

    public MediaTime Duration { get; }

    public ReadOnlyMemory<byte> Levels { get; }

    public ReadOnlyMemory<float> Waveform { get; }
}
