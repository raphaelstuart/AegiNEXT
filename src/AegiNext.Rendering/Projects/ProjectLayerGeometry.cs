using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>实际绘制内容与编辑画布共享的局部边界、轴心及父子变换；字形边界不包括描边和阴影。</summary>
public sealed record ProjectLayerGeometry(SKRect LocalBounds, SKPoint LocalPivot, SKPoint BasePosition,
    SKMatrix LocalToWorld, SKMatrix ParentToWorld, bool HasInk = true)
{
    public IReadOnlyList<SubtitleGraphemeGeometry>? VisibleGraphemes { get; init; }
    public SKPoint WorldPivot => LocalToWorld.MapPoint(LocalPivot);
    public IReadOnlyList<SKPoint> WorldCorners =>
    [
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Bottom),
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Bottom)
    ];

    /// <summary>按外层与文字局部变换逆映射测试可见字素，奇异变换不产生虚假命中。</summary>
    public bool ContainsWorldPoint(SKPoint point)
    {
        if (!LocalToWorld.TryInvert(out var outer))
        {
            return false;
        }
        var local = outer.MapPoint(point);
        if (VisibleGraphemes is null)
        {
            return LocalBounds.Contains(local);
        }
        foreach (var glyph in VisibleGraphemes)
        {
            if (glyph.UntransformedBounds is { } bounds)
            {
                if (glyph.LocalToVisible.TryInvert(out var inverse) && bounds.Contains(inverse.MapPoint(local)))
                {
                    return true;
                }
            }
            else if (glyph.Bounds.Contains(local))
            {
                return true;
            }
        }
        return false;
    }
}
