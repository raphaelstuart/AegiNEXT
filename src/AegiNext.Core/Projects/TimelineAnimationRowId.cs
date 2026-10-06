namespace AegiNext.Core.Projects;

/// <summary>属性行的稳定身份；同一字幕轨道的各片段共用同属性行。</summary>
public sealed record TimelineAnimationRowId(TimelineRowScope Scope, Guid OwnerId, AnimationProperty Property);
