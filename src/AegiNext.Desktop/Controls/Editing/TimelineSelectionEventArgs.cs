namespace AegiNext.Desktop.Controls;

/// <summary>时间线字幕选择。</summary>
public sealed class TimelineSelectionEventArgs : EventArgs
{
    /// <summary>创建选择请求。</summary>
    public TimelineSelectionEventArgs(Guid id, IReadOnlyList<Guid>? selectedIds = null)
    {
        Id = id;
        SelectedIds = selectedIds ?? [id];
    }

    public Guid Id { get; }
    public IReadOnlyList<Guid> SelectedIds { get; }
    public bool SelectionAccepted { get; set; } = true;
}
