using AegiNext.Core.Timing;

namespace AegiNext.Core.Media;

/// <summary>
/// 原始刻度与独立报告秒数；未知值不补零，报告时长不代表精确的可播放范围。
/// </summary>
public sealed record MediaStreamTiming
{
    public MediaTimeBase? TimeBase { get; init; }

    public long? StartPts { get; init; }

    public MediaTimestamp? StartTimestamp => StartPts is { } value && TimeBase is { } timeBase
        ? new(value, timeBase)
        : null;

    public long? DurationTicks { get; init; }

    public MediaTime? ReportedStart { get; init; }

    public MediaTime? ReportedDuration { get; init; }
}
