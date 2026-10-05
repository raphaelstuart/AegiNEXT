using AegiNext.Core.Editing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一次可撤销事务合并选区已有片段的高亮视觉。</summary>
    public void ApplySubtitleKaraokeStyleRange(Guid subtitleId, int utf16Start, int utf16Length,
        KaraokeVisualStyleEdit edit)
    {
        Apply("Apply karaoke character style", document => ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(
            document, subtitleId, utf16Start, utf16Length, edit));
    }

    /// <summary>以一次可撤销事务清除选区已有片段的高亮覆盖。</summary>
    public void ClearSubtitleKaraokeStyleRange(Guid subtitleId, int utf16Start, int utf16Length)
    {
        Apply("Clear karaoke character style", document => ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(
            document, subtitleId, utf16Start, utf16Length));
    }
}
