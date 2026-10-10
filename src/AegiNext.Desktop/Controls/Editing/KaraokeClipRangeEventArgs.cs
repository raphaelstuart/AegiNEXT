using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>基于按下时字幕、内容偏移与联动模式提交一次计时范围编辑。</summary>
public sealed class KaraokeClipRangeEventArgs(SubtitleLine baselineLine, MediaTime animationOffset,
    Guid clipId, MediaTime start, MediaTime end, bool isTimingLinked = false) : EventArgs
{
    public SubtitleLine BaselineLine { get; } = baselineLine;
    public MediaTime AnimationOffset { get; } = animationOffset;
    public Guid SubtitleId => BaselineLine.Id;
    public Guid ClipId { get; } = clipId;
    public MediaTime Start { get; } = start;
    public MediaTime End { get; } = end;
    /// <summary>按下时冻结的计时联动模式。</summary>
    public bool IsTimingLinked { get; } = isTimingLinked;
}
