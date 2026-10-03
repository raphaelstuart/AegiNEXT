using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>相对层起点的关键帧；Interpolation 控制从本帧到下一帧的区间。</summary>
public sealed record Keyframe(MediaTime Time, double Value, KeyframeInterpolation Interpolation = KeyframeInterpolation.LINEAR);
