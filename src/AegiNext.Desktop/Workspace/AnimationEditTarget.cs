using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

/// <summary>一次属性或手势编辑的固定图层及局部动画时刻。</summary>
internal sealed record AnimationEditTarget(Guid LayerId, MediaTime LocalTime, bool IsKeyframe);
