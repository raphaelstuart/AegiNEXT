using System.Collections.Immutable;
using System.Text.Json.Serialization;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>相对层内容时钟的完整属性关键帧；主插值控制第一分量并作为其他分量的默认曲线。</summary>
public sealed record Keyframe(MediaTime Time, AnimationValue Value, KeyframeInterpolation Interpolation = KeyframeInterpolation.LINEAR)
{
    public double CurveStart { get; init; }
    public double CurveEnd { get; init; } = 1;
    public ImmutableArray<AnimationCurve?> ComponentCurves { get; init; } = [];

    [JsonIgnore]
    public AnimationCurve? VectorCurve
    {
        get => ComponentCurves.Length == 1 ? ComponentCurves[0] : null;
        init => ComponentCurves = value is null ? [] : [value];
    }

    /// <summary>读取指定分量的有效曲线，包括迁移保留的独立插值和裁剪相位。</summary>
    public AnimationCurve GetCurve(int component)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(component);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(component, Value.ComponentCount);
        return component > 0 && !ComponentCurves.IsDefaultOrEmpty && ComponentCurves[component - 1] is { } curve
            ? curve : new(Interpolation, CurveStart, CurveEnd);
    }
}
