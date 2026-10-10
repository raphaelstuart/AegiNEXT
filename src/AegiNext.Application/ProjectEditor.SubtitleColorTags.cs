using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一个可撤销事务设置或清除选中字幕的工程颜色标记。</summary>
    public void SetSubtitleColorTag(IReadOnlyCollection<Guid> subtitleIds, Guid? tagId)
    {
        Apply("Set subtitle color tag", document => ProjectEditingOperations.SetSubtitleColorTag(document, subtitleIds, tagId));
    }

    /// <summary>以一个可撤销事务导入个人标记定义并赋给选中字幕，不同步已有工程定义。</summary>
    public void ApplySubtitleColorTag(IReadOnlyCollection<Guid> subtitleIds, SubtitleColorTag template)
    {
        Apply("Set subtitle color tag", document => ProjectEditingOperations.ApplySubtitleColorTag(document, subtitleIds, template));
    }
}
