using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>一次句前留白拖拽释放后的精确时长，相对字幕当前可见起点。</summary>
public sealed class KaraokeLeadingDelayEventArgs(Guid subtitleId, MediaTime delay) : EventArgs
{
    public Guid SubtitleId { get; } = subtitleId;
    public MediaTime Delay { get; } = delay;
}
