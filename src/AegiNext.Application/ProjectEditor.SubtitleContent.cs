using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>在一个可撤销事务内替换文字范围并同步局部样式与卡拉 OK。</summary>
    public void ReplaceSubtitleTextRange(Guid subtitleId, int utf16Start, int utf16Length, string replacement)
    {
        Apply("Edit subtitle text", document => ProjectEditingOperations.ReplaceSubtitleTextRange(
            document, subtitleId, utf16Start, utf16Length, replacement));
    }

    /// <summary>在一个可撤销事务内合并选区样式。</summary>
    public void ApplySubtitleInlineStyle(Guid subtitleId, int utf16Start, int utf16Length, SubtitleInlineStyleOverride style)
    {
        Apply("Apply subtitle inline style", document => ProjectEditingOperations.ApplySubtitleInlineStyle(
            document, subtitleId, utf16Start, utf16Length, style));
    }

    /// <summary>在一个可撤销事务内清除选区覆盖。</summary>
    public void ClearSubtitleInlineStyle(Guid subtitleId, int utf16Start, int utf16Length)
    {
        Apply("Clear subtitle inline style", document => ProjectEditingOperations.ClearSubtitleInlineStyle(
            document, subtitleId, utf16Start, utf16Length));
    }

    /// <summary>应用整行样式，清除局部覆盖并保留卡拉 OK。</summary>
    public void ApplySubtitleStyle(Guid subtitleId, SubtitleStyle style)
    {
        Apply("Apply subtitle style", document => ProjectEditingOperations.ApplySubtitleStyle(document, subtitleId, style));
    }

    /// <summary>以一个可撤销事务修改片段时长并顺延后续字时间。</summary>
    public void SetKaraokeClipDuration(Guid subtitleId, Guid clipId, MediaTime duration)
    {
        Apply("Edit karaoke duration", document => ProjectEditingOperations.SetKaraokeClipDuration(document, subtitleId, clipId, duration));
    }
}
