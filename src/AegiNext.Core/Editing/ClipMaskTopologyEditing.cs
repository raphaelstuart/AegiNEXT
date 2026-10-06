using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>按稳定身份编辑闭合蒙版拓扑；空轮廓不进入持久模型，固定变换和其余几何保持不变。</summary>
public static class ClipMaskTopologyEditing
{
    /// <summary>删除节点；删除轮廓最后一个节点时删除轮廓，删除最后一个轮廓时返回空蒙版。</summary>
    public static VectorClipMask? RemoveNode(VectorClipMask mask, Guid nodeId)
    {
        ArgumentNullException.ThrowIfNull(mask);
        for (var contourIndex = 0; contourIndex < mask.Contours.Length; contourIndex++)
        {
            var contour = mask.Contours[contourIndex];
            for (var nodeIndex = 0; nodeIndex < contour.Nodes.Length; nodeIndex++)
            {
                if (contour.Nodes[nodeIndex].Id != nodeId)
                {
                    continue;
                }

                return contour.Nodes.Length == 1 ? RemoveContourAt(mask, contourIndex) : mask with
                {
                    Contours = mask.Contours.SetItem(contourIndex, contour with { Nodes = contour.Nodes.RemoveAt(nodeIndex) })
                };
            }
        }

        throw new KeyNotFoundException($"蒙版节点不存在：{nodeId}。");
    }

    /// <summary>删除指定闭合轮廓；删除最后一个轮廓时返回空蒙版。</summary>
    public static VectorClipMask? RemoveContour(VectorClipMask mask, Guid contourId)
    {
        ArgumentNullException.ThrowIfNull(mask);
        for (var index = 0; index < mask.Contours.Length; index++)
        {
            if (mask.Contours[index].Id == contourId)
            {
                return RemoveContourAt(mask, index);
            }
        }

        throw new KeyNotFoundException($"蒙版轮廓不存在：{contourId}。");
    }

    private static VectorClipMask? RemoveContourAt(VectorClipMask mask, int index)
    {
        return mask.Contours.Length == 1 ? null : mask with { Contours = mask.Contours.RemoveAt(index) };
    }
}
