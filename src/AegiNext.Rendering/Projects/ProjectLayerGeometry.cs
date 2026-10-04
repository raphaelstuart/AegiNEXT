using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>实际绘制内容与编辑画布共享的局部边界、轴心及父子变换；字形边界不包括描边和阴影。</summary>
public sealed record ProjectLayerGeometry(SKRect LocalBounds, SKPoint LocalPivot, SKPoint BasePosition,
    SKMatrix LocalToWorld, SKMatrix ParentToWorld, bool HasInk = true)
{
    public SKPoint WorldPivot => LocalToWorld.MapPoint(LocalPivot);
    public IReadOnlyList<SKPoint> WorldCorners =>
    [
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Top),
        LocalToWorld.MapPoint(LocalBounds.Right, LocalBounds.Bottom),
        LocalToWorld.MapPoint(LocalBounds.Left, LocalBounds.Bottom)
    ];
}
