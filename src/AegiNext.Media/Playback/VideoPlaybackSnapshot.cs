using AegiNext.Core.Timing;

namespace AegiNext.Media.Playback;

/// <summary>
/// 不可变的会话快照；位置来自单调时钟，显示时间保留实际交付的原始帧时间。
/// </summary>
public sealed record VideoPlaybackSnapshot(
    VideoPlaybackState State,
    long Generation,
    MediaTime Position,
    MediaTime? DisplayTime,
    Exception? Error);
