using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>工程坐标蒙版草稿的完整值及固定 Clip 身份。</summary>
public sealed class CanvasMaskEditEventArgs(Guid layerId, ClipMask mask, Guid? nodeId = null) : EventArgs
{
    public Guid LayerId { get; } = layerId;
    public ClipMask Mask { get; } = mask;
    public Guid? NodeId { get; } = nodeId;
}
