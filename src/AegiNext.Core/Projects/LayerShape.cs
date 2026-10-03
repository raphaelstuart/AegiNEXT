namespace AegiNext.Core.Projects;

/// <summary>矩形、椭圆或路径形状；Width/Height 为局部像素尺寸。</summary>
public sealed record LayerShape(ShapeKind Kind, double Width, double Height, PathGeometry? Path = null);
