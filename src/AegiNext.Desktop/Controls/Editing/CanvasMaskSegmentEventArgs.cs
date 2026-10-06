namespace AegiNext.Desktop.Controls;

/// <summary>冻结字幕、轮廓及线段起始节点身份的蒙版细分请求。</summary>
public sealed class CanvasMaskSegmentEventArgs(Guid layerId, Guid contourId, Guid nodeId, double progress = 0.5) : EventArgs
{
    public Guid LayerId { get; } = layerId;
    public Guid ContourId { get; } = contourId;
    public Guid NodeId { get; } = nodeId;
    public double Progress { get; } = progress;
}
