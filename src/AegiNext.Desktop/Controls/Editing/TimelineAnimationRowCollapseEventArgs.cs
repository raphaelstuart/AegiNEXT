using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>请求所属会话更新属性行的折叠状态，不直接修改工程或内容历史。</summary>
public sealed class TimelineAnimationRowCollapseEventArgs(TimelineAnimationRowId id, bool isCollapsed) : EventArgs
{
    public TimelineAnimationRowId Id { get; } = id;
    public bool IsCollapsed { get; } = isCollapsed;
}
