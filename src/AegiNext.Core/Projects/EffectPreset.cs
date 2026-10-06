using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>可复用的局部动画、运动路径与混合设置；字幕片段保留自己的蒙版几何。</summary>
public sealed record EffectPreset(Guid Id, string Name, ImmutableArray<AnimationTrack> Tracks,
    MotionPath? MotionPath = null, BlendMode Blend = BlendMode.NORMAL);
