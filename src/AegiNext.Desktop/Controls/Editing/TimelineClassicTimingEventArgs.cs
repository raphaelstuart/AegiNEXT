using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>经典鼠标打轴对当前主选字幕的一次边界修改。</summary>
public sealed class TimelineClassicTimingEventArgs(Guid cueId, MediaTime time, bool isStart) : EventArgs
{
    public Guid CueId { get; } = cueId;
    public MediaTime Time { get; } = time;
    public bool IsStart { get; } = isStart;
}
