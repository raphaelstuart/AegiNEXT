using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>画布拖拽完成后的局部层数据；一次拖拽提交一次事务。</summary>
public sealed class CanvasLayerEditEventArgs : EventArgs
{
    /// <summary>创建层变换或路径编辑结果。</summary>
    public CanvasLayerEditEventArgs(Guid layerId, LayerTransform transform, MotionPath? path)
    {
        LayerId = layerId;
        Transform = transform;
        Path = path;
    }

    public Guid LayerId { get; }
    public LayerTransform Transform { get; }
    public MotionPath? Path { get; }
}
