using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>局部像素坐标中的三次贝塞尔路径；Closed 控制填充与末端闭合。</summary>
public sealed record PathGeometry(ScenePoint Start, ImmutableArray<CubicBezierSegment> Segments, bool Closed = false);
