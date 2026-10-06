namespace AegiNext.Core.Projects;

/// <summary>动画轨道的完整身份；节点属性使用稳定节点标识，普通属性不携带节点。</summary>
public readonly record struct AnimationTrackTarget(AnimationProperty Property, Guid? NodeId = null);
