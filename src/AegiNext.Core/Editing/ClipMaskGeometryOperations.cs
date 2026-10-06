using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>具有稳定节点身份的闭合蒙版曲线编辑。</summary>
public static class ClipMaskGeometryOperations
{
    /// <summary>在指定节点到下一节点的曲线参数处细分，保留形状、轮廓身份及独立变换。</summary>
    public static VectorClipMask SubdivideSegment(VectorClipMask mask, Guid contourId, Guid nodeId, double progress = 0.5)
    {
        ArgumentNullException.ThrowIfNull(mask);
        if (!double.IsFinite(progress) || progress <= 0 || progress >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(progress));
        }
        var contour = mask.Contours.FirstOrDefault(item => item.Id == contourId) ?? throw new ArgumentOutOfRangeException(nameof(contourId));
        var contourIndex = mask.Contours.IndexOf(contour);
        var first = contour.Nodes.FirstOrDefault(node => node.Id == nodeId) ?? throw new ArgumentOutOfRangeException(nameof(nodeId));
        var index = contour.Nodes.IndexOf(first);
        if (mask.Contours.Sum(item => (long)item.Nodes.Length) >= 10000)
        {
            throw new InvalidOperationException("蒙版节点总量超过预算。");
        }
        var nextIndex = (index + 1) % contour.Nodes.Length;
        var next = contour.Nodes[nextIndex];
        var split = CubicBezierSubdivision.Split(first.Position,
            new(Add(first.Position, first.OutHandle), Add(next.Position, next.InHandle), next.Position), progress);
        var left = split.Before;
        var right = split.After;
        var inserted = new MaskNode
        {
            Position = left.End, InHandle = Subtract(left.Control2, left.End), OutHandle = Subtract(right.Control1, left.End)
        };
        var nodes = contour.Nodes.SetItem(index, first with { OutHandle = Subtract(left.Control1, first.Position) });
        nodes = nodes.SetItem(nextIndex, nodes[nextIndex] with { InHandle = Subtract(right.Control2, next.Position) }).Insert(index + 1, inserted);
        return mask with { Contours = mask.Contours.SetItem(contourIndex, contour with { Nodes = nodes }) };
    }

    private static ScenePoint Add(ScenePoint first, ScenePoint second) => new(first.X + second.X, first.Y + second.Y);
    private static ScenePoint Subtract(ScenePoint first, ScenePoint second) => new(first.X - second.X, first.Y - second.Y);
}
