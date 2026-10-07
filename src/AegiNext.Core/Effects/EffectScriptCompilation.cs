using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Effects;

internal sealed record EffectScriptCompilation(ImmutableArray<AnimationTrack> Tracks,
    ImmutableArray<EffectScriptInterval> Intervals);
