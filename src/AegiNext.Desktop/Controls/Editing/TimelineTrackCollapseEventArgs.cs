namespace AegiNext.Desktop.Controls;

/// <summary>请求所属会话更新整体轨道折叠状态，不直接修改工程内容或撤销历史。</summary>
public sealed class TimelineTrackCollapseEventArgs(Guid id, bool isCollapsed) : EventArgs
{
    public Guid Id { get; } = id;
    public bool IsCollapsed { get; } = isCollapsed;
}
