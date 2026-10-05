using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

/// <summary>段内归一化位置的属性值；插值方式控制至下一个关键帧的区间。</summary>
public sealed record EffectScriptKeyframe(decimal Progress, EffectScriptProperty Property, EffectScriptValue Value,
    KeyframeInterpolation Interpolation = KeyframeInterpolation.LINEAR, int Line = 0, int Column = 1);
