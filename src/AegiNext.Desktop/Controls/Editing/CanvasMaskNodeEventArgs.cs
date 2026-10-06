namespace AegiNext.Desktop.Controls;

/// <summary>冻结字幕 Clip 与蒙版节点身份的画布编辑请求。</summary>
public sealed class CanvasMaskNodeEventArgs(Guid layerId, Guid nodeId) : EventArgs
{
    public Guid LayerId { get; } = layerId;
    public Guid NodeId { get; } = nodeId;
}
