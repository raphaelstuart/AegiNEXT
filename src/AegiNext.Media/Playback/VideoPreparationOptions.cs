using AegiNext.Core.Timing;

namespace AegiNext.Media.Playback;

/// <summary>限定提前解码的时间范围、未释放原始帧数量与像素内存。</summary>
public sealed record VideoPreparationOptions
{
    /// <summary>创建默认至多两帧、提前 250 毫秒、原始帧总计 128 MiB 的准备预算。</summary>
    public VideoPreparationOptions(int pendingCapacity = 2, long maximumPendingBytes = 128L * 1024 * 1024,
        MediaTime? maximumAhead = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pendingCapacity);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumPendingBytes, pendingCapacity);
        var ahead = maximumAhead ?? new MediaTime(250, 1000);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(ahead, MediaTime.Zero);
        PendingCapacity = pendingCapacity;
        MaximumPendingBytes = maximumPendingBytes;
        MaximumAhead = ahead;
    }

    public int PendingCapacity { get; }
    public long MaximumPendingBytes { get; }
    public MediaTime MaximumAhead { get; }
}
