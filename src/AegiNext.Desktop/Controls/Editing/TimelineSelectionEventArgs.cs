namespace AegiNext.Desktop.Controls;

/// <summary>时间线字幕选择。</summary>
public sealed class TimelineSelectionEventArgs : EventArgs
{
    /// <summary>创建选择请求。</summary>
    public TimelineSelectionEventArgs(Guid id) => Id = id;

    public Guid Id { get; }
}
