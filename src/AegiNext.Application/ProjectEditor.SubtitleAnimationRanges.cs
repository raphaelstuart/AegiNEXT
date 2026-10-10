using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>在一个可撤销事务中创建或更新文本动画范围和覆盖顺序。</summary>
    public void SetSubtitleAnimationRange(Guid subtitleId, SubtitleAnimationRange range, int? index = null)
    {
        Apply("Edit subtitle animation range", document => ProjectEditingOperations.SetSubtitleAnimationRange(document, subtitleId, range, index));
    }

    /// <summary>在一个可撤销事务中删除范围并清理它的所有动画轨道。</summary>
    public void RemoveSubtitleAnimationRange(Guid subtitleId, Guid rangeId)
    {
        Apply("Remove subtitle animation range", document => ProjectEditingOperations.RemoveSubtitleAnimationRange(document, subtitleId, rangeId));
    }

    /// <summary>在一个可撤销事务中调整文本动画范围覆盖顺序。</summary>
    public void MoveSubtitleAnimationRange(Guid subtitleId, Guid rangeId, int index)
    {
        Apply("Move subtitle animation range", document => ProjectEditingOperations.MoveSubtitleAnimationRange(document, subtitleId, rangeId, index));
    }
}
