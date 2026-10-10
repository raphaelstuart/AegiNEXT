using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>合并完整字素选区的激活视觉覆盖，不创建计时或拆分计时组。</summary>
    public static ProjectDocument ApplySubtitleKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length, KaraokeVisualStyleEdit edit)
    {
        return ApplySubtitleKaraokeStyleRange(document, subtitleId, utf16Start, utf16Length,
            KaraokeVisualState.ACTIVE, edit);
    }

    /// <summary>合并完整字素选区指定状态的视觉覆盖，未计时和禁用文字同样可以保存样式。</summary>
    public static ProjectDocument ApplySubtitleKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length, KaraokeVisualState state, KaraokeVisualStyleEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return EditKaraokeStyleRange(document, subtitleId, utf16Start, utf16Length, state, edit);
    }

    /// <summary>清除选区的激活覆盖，重新继承整句高亮及原片段默认填充。</summary>
    public static ProjectDocument ClearSubtitleKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length)
    {
        return ClearSubtitleKaraokeStyleRange(document, subtitleId, utf16Start, utf16Length, KaraokeVisualState.ACTIVE);
    }

    /// <summary>清除选区指定状态的范围覆盖，保留另一状态及全部计时数据。</summary>
    public static ProjectDocument ClearSubtitleKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length, KaraokeVisualState state)
    {
        return EditKaraokeStyleRange(document, subtitleId, utf16Start, utf16Length, state, null);
    }

    private static ProjectDocument EditKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int start, int length, KaraokeVisualState state, KaraokeVisualStyleEdit? edit)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = SubtitleKaraokeStyleEditing.Edit(document.Subtitles[index], start, length, state, edit);
        return WithSubtitleContent(document, index, line);
    }
}
