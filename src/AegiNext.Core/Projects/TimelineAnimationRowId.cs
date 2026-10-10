using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>属性行的稳定身份；轨道整行属性按视觉状态共用，文字范围属性由工程唯一范围标识与视觉状态独立区分。</summary>
public sealed record TimelineAnimationRowId(TimelineRowScope Scope, Guid OwnerId, AnimationProperty Property,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? TextRangeId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] SubtitleAnimationState State = SubtitleAnimationState.NORMAL);
