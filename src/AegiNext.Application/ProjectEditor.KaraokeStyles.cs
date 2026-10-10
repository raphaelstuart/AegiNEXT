using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一次可撤销事务合并选区的激活视觉覆盖。</summary>
    public void ApplySubtitleKaraokeStyleRange(Guid subtitleId, int utf16Start, int utf16Length,
        KaraokeVisualStyleEdit edit)
    {
        ApplySubtitleKaraokeStyleRange(subtitleId, utf16Start, utf16Length, KaraokeVisualState.ACTIVE, edit);
    }

    /// <summary>以一次可撤销事务合并选区指定状态的视觉覆盖。</summary>
    public void ApplySubtitleKaraokeStyleRange(Guid subtitleId, int utf16Start, int utf16Length,
        KaraokeVisualState state, KaraokeVisualStyleEdit edit)
    {
        Apply("Apply karaoke character style", document => ProjectEditingOperations.ApplySubtitleKaraokeStyleRange(
            document, subtitleId, utf16Start, utf16Length, state, edit));
    }

    /// <summary>以一次可撤销事务清除选区的激活覆盖。</summary>
    public void ClearSubtitleKaraokeStyleRange(Guid subtitleId, int utf16Start, int utf16Length)
    {
        ClearSubtitleKaraokeStyleRange(subtitleId, utf16Start, utf16Length, KaraokeVisualState.ACTIVE);
    }

    /// <summary>以一次可撤销事务清除选区指定状态的视觉覆盖。</summary>
    public void ClearSubtitleKaraokeStyleRange(Guid subtitleId, int utf16Start, int utf16Length, KaraokeVisualState state)
    {
        Apply("Clear karaoke character style", document => ProjectEditingOperations.ClearSubtitleKaraokeStyleRange(
            document, subtitleId, utf16Start, utf16Length, state));
    }
}
