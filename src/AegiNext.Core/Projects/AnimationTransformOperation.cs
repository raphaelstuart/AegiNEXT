using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>按轨道数组顺序求值的原生变换操作；稳定标识用于编辑，时间使用内容时钟。</summary>
public sealed record AnimationTransformOperation(Guid Id, MediaTime Start, MediaTime End, AnimationValue Value, double Acceleration = 1);
