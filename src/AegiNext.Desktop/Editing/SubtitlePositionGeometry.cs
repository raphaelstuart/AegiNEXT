using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

/// <summary>编辑锚点所需的父画布、实际字形边界和局部变换；不持有任何渲染资源。</summary>
public sealed record SubtitlePositionGeometry(ScenePoint ParentSize, ScenePoint GlyphOrigin,
    ScenePoint GlyphSize, LayerTransform Transform, bool HasInk = true)
{
    /// <summary>将规范化 pivot 的改变量转换为旋转、缩放后的父局部像素位移。</summary>
    public ScenePoint PivotDisplacement(ScenePoint previous, ScenePoint next)
    {
        var x = (next.X - previous.X) * GlyphSize.X * Transform.ScaleX;
        var y = (next.Y - previous.Y) * GlyphSize.Y * Transform.ScaleY;
        var angle = Transform.Rotation * Math.PI / 180;
        var cosine = Math.Cos(angle);
        var sine = Math.Sin(angle);
        return new(cosine * x - sine * y, sine * x + cosine * y);
    }
}
