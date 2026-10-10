using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

/// <summary>固定时长段或按权重分配剩余时间的自由段；空段保持前一段的属性值。</summary>
public sealed record EffectScriptSegment(string Name, MediaTime? FixedDuration, decimal FlexWeight,
    ImmutableArray<EffectScriptKeyframe> Keyframes, int Line = 0, int Column = 1)
{
    public int RepeatCount { get; init; } = 1;
    public bool PingPong { get; init; }
    public MediaTime? CycleDuration { get; init; }
}
