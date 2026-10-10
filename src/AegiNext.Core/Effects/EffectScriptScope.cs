using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

/// <summary>命名作用范围及其分组、启动时序和独立时间段。</summary>
public sealed record EffectScriptScope(string Name, EffectScriptTargetSelector Target,
    ImmutableArray<EffectScriptSegment> Segments, int Line = 0, int Column = 1)
{
    public EffectScriptUnit Unit { get; init; } = new(EffectScriptUnitKind.GROUP);
    public MediaTime Delay { get; init; } = MediaTime.Zero;
    public MediaTime Stagger { get; init; } = MediaTime.Zero;
    public EffectScriptOrder Order { get; init; } = EffectScriptOrder.FORWARD;
    public SubtitleAnimationState State { get; init; } = SubtitleAnimationState.NORMAL;
}
