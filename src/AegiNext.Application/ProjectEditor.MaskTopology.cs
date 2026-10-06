using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>按稳定节点身份删除闭合蒙版节点；空轮廓及最后的蒙版轨道在同一事务清理。</summary>
    public void RemoveClipMaskNode(Guid layerId, Guid nodeId)
    {
        EditClipMaskTopology(layerId, nodeId, ClipMaskTopologyEditing.RemoveNode, "Remove clip mask node");
    }

    /// <summary>按稳定轮廓身份删除闭合蒙版轮廓；最后轮廓及蒙版轨道在同一事务清理。</summary>
    public void RemoveClipMaskContour(Guid layerId, Guid contourId)
    {
        EditClipMaskTopology(layerId, contourId, ClipMaskTopologyEditing.RemoveContour, "Remove clip mask contour");
    }

    private void EditClipMaskTopology(Guid layerId, Guid identity,
        Func<VectorClipMask, Guid, VectorClipMask?> edit, string label)
    {
        Apply(label, document =>
        {
            var layer = FindLayer(document.Layers, layerId);
            RequireSubtitleMaskTarget(document, layer);
            if (layer.Mask is not VectorClipMask vector)
            {
                throw new InvalidOperationException("只有闭合自由路径蒙版可以删除节点或轮廓。");
            }

            if (ClipMaskAnimation.IsTopologyLocked(layer))
            {
                throw new InvalidOperationException("存在节点形变动画时不能改变蒙版拓扑；请先清除节点形变轨道。");
            }

            var mask = edit(vector, identity);
            var next = layer with
            {
                Mask = mask,
                Tracks = mask is null ? layer.Tracks.Where(track => !AnimationPropertyMetadata.IsMaskProperty(track.Property)).ToImmutableArray() : layer.Tracks
            };
            return document with
            {
                Layers = MapLayers(document.Layers, value => value.Id == layerId ? next : value)
            };
        });
    }
}
