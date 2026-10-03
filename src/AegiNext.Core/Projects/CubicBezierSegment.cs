namespace AegiNext.Core.Projects;

/// <summary>从上一段终点开始的三次贝塞尔曲线。</summary>
public sealed record CubicBezierSegment(ScenePoint Control1, ScenePoint Control2, ScenePoint End);
