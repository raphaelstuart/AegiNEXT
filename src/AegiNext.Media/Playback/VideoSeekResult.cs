using AegiNext.Core.Timing;

namespace AegiNext.Media.Playback;

/// <summary>
/// 精确定位的时间与边界结果；不包含额外的帧所有权。
/// </summary>
public sealed record VideoSeekResult(
    MediaTime RequestedTime,
    MediaTime? SelectedTime,
    MediaTime? NextFrameTime,
    long Generation,
    bool IsBeforeFirst,
    bool ReachedEnd);
