using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>同一完整目标的动画；严格递增关键帧与保留源顺序的变换操作互斥。</summary>
[method: JsonConstructor]
public sealed record AnimationTrack(AnimationTrackTarget Target, ImmutableArray<Keyframe> Keyframes)
{
    public AnimationValue? InitialValue { get; init; }
    public ImmutableArray<AnimationTransformOperation> Transforms { get; init; } = [];

    [JsonIgnore]
    public bool IsOrdered => !Transforms.IsDefaultOrEmpty;

    [JsonIgnore]
    public AnimationProperty Property => Target.Property;

    /// <summary>创建不携带节点标识的普通属性轨道。</summary>
    public AnimationTrack(AnimationProperty property, ImmutableArray<Keyframe> keyframes)
        : this(new AnimationTrackTarget(property), keyframes)
    {
    }
}
