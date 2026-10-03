using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>可复用的局部动画、路径、蒙版与混合设置；应用时保留节点与字幕标识。</summary>
public sealed record EffectPreset(Guid Id, string Name, ImmutableArray<AnimationTrack> Tracks,
    MotionPath? MotionPath = null, LayerMask? Mask = null, BlendMode Blend = BlendMode.NORMAL);
