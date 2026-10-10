using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>多分量动画的独立插值和裁剪相位；保留旧通道动画的精确结果。</summary>
public sealed record AnimationCurve(KeyframeInterpolation Interpolation, double CurveStart = 0, double CurveEnd = 1)
{
    public double Exponent { get; init; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Reverse { get; init; }
}
