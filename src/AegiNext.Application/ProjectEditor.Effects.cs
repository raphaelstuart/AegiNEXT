using AegiNext.Core.Effects;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>一次清除所选非组片段的全部动画轨道；无变化不新增历史，未知身份整批拒绝。</summary>
    public void ClearAnimationTracks(IReadOnlyCollection<Guid> layerIds)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        var selection = layerIds.ToArray();
        Apply("Clear clip animation tracks", document => ProjectEditingOperations.ClearAnimationTracks(document, selection));
    }

    /// <summary>一次清除指定图层该属性的全部关键帧、有序变换和节点目标，保留其他动画及静态内容。</summary>
    public void ClearAnimationTracks(IReadOnlyCollection<Guid> layerIds, AnimationProperty property)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        var selection = layerIds.ToArray();
        Apply("Clear animation property tracks", document => ProjectEditingOperations.ClearAnimationTracks(document, selection, property));
    }

    /// <summary>在一个可撤销事务中清除完整动画目标，保留同属性的其他范围和状态。</summary>
    public void ClearAnimationTracks(IReadOnlyCollection<Guid> layerIds, AnimationTrackTarget target)
    {
        ArgumentNullException.ThrowIfNull(layerIds);
        var selection = layerIds.ToArray();
        Apply("Clear animation target tracks", document => ProjectEditingOperations.ClearAnimationTracks(document, selection, target));
    }

    /// <summary>按目标片段编译并原子组合脚本；保留未声明区间的既有动画、内容身份和路径。</summary>
    public void ApplyEffectScript(Guid layerId, EffectScript script, AnimationTrackTarget? targetContext = null)
    {
        ApplyEffectScript([layerId], script, targetContext);
    }

    /// <summary>按每个目标片段的时长和基础值编译并一次提交全部脚本轨道；任一目标失败不修改快照或历史。</summary>
    public void ApplyEffectScript(IReadOnlyCollection<Guid> layerIds, EffectScript script, AnimationTrackTarget? targetContext = null)
    {
        Apply("Apply effect script", document => ProjectEditingOperations.ApplyEffectScript(document, layerIds, script,
            targetContext: targetContext));
    }
}
