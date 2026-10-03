namespace AegiNext.Core.Projects;

/// <summary>在层的局部坐标应用路径蒙版，可反相。</summary>
public sealed record LayerMask(PathGeometry Path, bool Inverted = false);
