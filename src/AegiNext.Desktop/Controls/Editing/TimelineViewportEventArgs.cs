using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Controls;

/// <summary>只改变编辑视窗，不改变工程或播放时间。</summary>
public sealed class TimelineViewportEventArgs(TimelineViewport viewport, bool isUserInitiated = true) : EventArgs
{
    public TimelineViewport Viewport { get; } = viewport;

    /// <summary>区分用户导航和布局、选择等程序同步，供调用方管理播放跟随状态。</summary>
    public bool IsUserInitiated { get; } = isUserInitiated;
}
