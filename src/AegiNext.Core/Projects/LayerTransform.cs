namespace AegiNext.Core.Projects;

/// <summary>局部锚点平移至原点后缩放、按度旋转，再加 X/Y；父组变换在外层应用。</summary>
public sealed record LayerTransform(double X = 0, double Y = 0, double ScaleX = 1, double ScaleY = 1,
    double Rotation = 0, double AnchorX = 0, double AnchorY = 0);
