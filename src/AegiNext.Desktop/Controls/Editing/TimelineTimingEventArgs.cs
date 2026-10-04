using AegiNext.Core.Editing;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>一次完成的移动、裁剪或显式时间拉伸。</summary>
public sealed class TimelineTimingEventArgs : EventArgs
{
    /// <summary>保存原始和最终范围，供应用层按编辑模式提交一次撤销记录。</summary>
    public TimelineTimingEventArgs(Guid id, MediaTime originalStart, MediaTime start, MediaTime end, TimelineEditMode mode, bool isMove)
    {
        Id = id;
        OriginalStart = originalStart;
        Start = start;
        End = end;
        Mode = mode;
        IsMove = isMove;
    }

    public Guid Id { get; }
    public MediaTime OriginalStart { get; }
    public MediaTime Start { get; }
    public MediaTime End { get; }
    public TimelineEditMode Mode { get; }
    public bool IsMove { get; }
    public Guid? SubtitleId { get; init; }
    public Guid? TrackId { get; init; }
}
