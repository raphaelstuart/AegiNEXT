using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>基于按下时字幕与内容偏移提交一次独立计时范围编辑。</summary>
public sealed class KaraokeClipRangeEventArgs(SubtitleLine baselineLine, MediaTime animationOffset,
    Guid clipId, MediaTime start, MediaTime end) : EventArgs
{
    public SubtitleLine BaselineLine { get; } = baselineLine;
    public MediaTime AnimationOffset { get; } = animationOffset;
    public Guid SubtitleId => BaselineLine.Id;
    public Guid ClipId { get; } = clipId;
    public MediaTime Start { get; } = start;
    public MediaTime End { get; } = end;
}
