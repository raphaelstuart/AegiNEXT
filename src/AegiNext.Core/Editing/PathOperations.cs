using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>字幕运动路径的纯数据编辑；所有节点保持工程像素坐标。</summary>
public static class PathOperations
{
    /// <summary>追加路径端点，初始控制柄形成直线，可继续独立编辑。</summary>
    public static PathGeometry AppendPoint(PathGeometry path, ScenePoint point)
    {
        Validate(path);
        Validate(point);
        if (path.Segments.Length >= 10000)
        {
            throw new InvalidOperationException("路径已经达到节点数量限制。");
        }

        var start = path.Segments[^1].End;
        return path with { Segments = path.Segments.Add(new(Lerp(start, point, 1d / 3), Lerp(start, point, 2d / 3), point)) };
    }

    /// <summary>用 de Casteljau 算法插入端点，精确保持原贝塞尔形状。</summary>
    public static PathGeometry SplitSegment(PathGeometry path, int segmentIndex, double progress = 0.5)
    {
        Validate(path);
        if (segmentIndex < 0 || segmentIndex >= path.Segments.Length || !double.IsFinite(progress) || progress <= 0 || progress >= 1)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex), "路径段或插入位置无效。");
        }

        if (path.Segments.Length >= 10000)
        {
            throw new InvalidOperationException("路径已经达到节点数量限制。");
        }

        var start = segmentIndex == 0 ? path.Start : path.Segments[segmentIndex - 1].End;
        var segment = path.Segments[segmentIndex];
        var split = CubicBezierSubdivision.Split(start, segment, progress);
        return path with
        {
            Segments = path.Segments.SetItem(segmentIndex, split.Before).Insert(segmentIndex + 1, split.After)
        };
    }

    /// <summary>移除起点或端点；内部端点以两侧外部控制柄连接相邻曲线。</summary>
    public static PathGeometry RemovePoint(PathGeometry path, int pointIndex)
    {
        Validate(path);
        if (pointIndex < 0 || pointIndex > path.Segments.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(pointIndex));
        }

        if (path.Segments.Length == 1)
        {
            throw new InvalidOperationException("运动路径至少保留两个端点。");
        }

        if (pointIndex == 0)
        {
            return path with { Start = path.Segments[0].End, Segments = path.Segments.RemoveAt(0) };
        }

        if (pointIndex == path.Segments.Length)
        {
            return path with { Segments = path.Segments.RemoveAt(pointIndex - 1) };
        }

        var before = path.Segments[pointIndex - 1];
        var after = path.Segments[pointIndex];
        return path with
        {
            Segments = path.Segments.SetItem(pointIndex - 1, new(before.Control1, after.Control2, after.End)).RemoveAt(pointIndex)
        };
    }

    private static ScenePoint Lerp(ScenePoint first, ScenePoint second, double progress) =>
        new(first.X + (second.X - first.X) * progress, first.Y + (second.Y - first.Y) * progress);

    private static void Validate(PathGeometry path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Segments.IsDefaultOrEmpty || path.Segments.Length > 10000)
        {
            throw new ArgumentException("路径节点集合无效。", nameof(path));
        }

        Validate(path.Start);
        foreach (var segment in path.Segments)
        {
            Validate(segment.Control1);
            Validate(segment.Control2);
            Validate(segment.End);
        }
    }

    private static void Validate(ScenePoint point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > 1e9 || Math.Abs(point.Y) > 1e9)
        {
            throw new ArgumentOutOfRangeException(nameof(point), "路径坐标无效。");
        }
    }
}
