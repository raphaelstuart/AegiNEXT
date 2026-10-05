using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>脚本值只有一个带维度的字面量；base 直接引用应用前基础值。</summary>
public sealed record EffectScriptValue(EffectScriptValueKind Kind, AnimationValue? Literal = null)
{
    /// <summary>由标量或二维向量构造现有脚本字面量。</summary>
    public EffectScriptValue(EffectScriptValueKind kind, double x, double? y = null)
        : this(kind, y is { } second ? AnimationValue.FromVector(new(x, second)) : AnimationValue.FromScalar(x))
    {
    }

    public double X => Literal is { } literal ? literal.GetComponent(0) : 0;
    public double? Y => Literal is { IsVector: true } literal ? literal.Vector.Y : null;
}
