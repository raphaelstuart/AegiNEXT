using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>按等段参数速度遍历的相对位移路径；并非弧长匀速，Duration 相对层起点。</summary>
public sealed record MotionPath(PathGeometry Path, MediaTime Duration, bool OrientToPath = false);
