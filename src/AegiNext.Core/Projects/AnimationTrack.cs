using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>同一属性的严格递增关键帧序列，序列以外保持端点值。</summary>
public sealed record AnimationTrack(AnimationProperty Property, ImmutableArray<Keyframe> Keyframes);
