using AegiNext.Core.Timing;

namespace AegiNext.Media.Decoding;

/// <summary>显示时间及可追溯依据；推导时间不会写入原始 PTS 或 best-effort 字段。</summary>
public sealed record VideoFrameDisplayTiming(MediaTimestamp Timestamp, VideoDisplayTimingEvidence Evidence)
{
    /// <summary>该显示时间是否包含帧时长、声明帧率或流起点推导。</summary>
    public bool IsDerived => (Evidence & (VideoDisplayTimingEvidence.PreviousFrameDuration |
        VideoDisplayTimingEvidence.DeclaredFrameRate | VideoDisplayTimingEvidence.StreamStart)) != 0;
}
