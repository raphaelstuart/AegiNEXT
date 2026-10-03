using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>时间线定位请求。</summary>
public sealed class TimelineSeekEventArgs : EventArgs
{
    /// <summary>创建定位请求。</summary>
    public TimelineSeekEventArgs(MediaTime time) => Time = time;

    public MediaTime Time { get; }
}
