namespace AegiNext.Media.Decoding;

/// <summary>显示时间使用的锚点与推导依据；不改变帧的原始时间戳。</summary>
[Flags]
public enum VideoDisplayTimingEvidence
{
    OriginalPts = 1,
    BestEffortTimestamp = 2,
    PreviousFrameDuration = 4,
    DeclaredFrameRate = 8,
    StreamStart = 16
}
