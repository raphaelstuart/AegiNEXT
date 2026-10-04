using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Controls;

/// <summary>只改变编辑视窗，不改变工程或播放时间。</summary>
public sealed class TimelineViewportEventArgs(TimelineViewport viewport) : EventArgs
{
    public TimelineViewport Viewport { get; } = viewport;
}
