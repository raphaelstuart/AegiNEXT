using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>画布资源或工程渲染失败时的语义通知，不携带控件或原生呈现资源。</summary>
public sealed class CanvasRenderingFailedEventArgs : EventArgs
{
    /// <summary>记录失败所属工程、工程时间及原始异常。</summary>
    public CanvasRenderingFailedEventArgs(Guid projectId, MediaTime position, Exception error)
    {
        ProjectId = projectId;
        Position = position;
        Error = error;
    }

    public Guid ProjectId { get; }
    public MediaTime Position { get; }
    public Exception Error { get; }
}
