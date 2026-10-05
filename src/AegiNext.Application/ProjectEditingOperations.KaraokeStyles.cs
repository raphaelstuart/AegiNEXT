using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>合并完整字素选区中已有片段的高亮视觉，不创建字时间或改变其他字的覆盖。</summary>
    public static ProjectDocument ApplySubtitleKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length, KaraokeVisualStyleEdit edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return EditKaraokeStyleRange(document, subtitleId, utf16Start, utf16Length, edit);
    }

    /// <summary>清除选区已有片段的激活覆盖，使其重新继承整句默认高亮及原片段填充。</summary>
    public static ProjectDocument ClearSubtitleKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length)
    {
        return EditKaraokeStyleRange(document, subtitleId, utf16Start, utf16Length, null);
    }

    private static ProjectDocument EditKaraokeStyleRange(ProjectDocument document, Guid subtitleId,
        int start, int length, KaraokeVisualStyleEdit? edit)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        SubtitleTextEditMap.ValidateRange(line.Text, SubtitleTextEditMap.Boundaries(line.Text), start, length);
        if (length == 0 || edit is { HasChanges: false })
        {
            return document;
        }
        line = SubtitleKaraokeNormalization.Normalize(line);
        var clips = line.Karaoke.ToBuilder();
        var spanIndex = 0;
        for (var cursor = 0; cursor < clips.Count; cursor++)
        {
            var clip = clips[cursor];
            if (clip.Utf16Start < start || clip.Utf16Start >= start + length)
            {
                continue;
            }
            while (spanIndex < line.InlineSpans.Length &&
                line.InlineSpans[spanIndex].Utf16Start + line.InlineSpans[spanIndex].Utf16Length <= clip.Utf16Start)
            {
                spanIndex++;
            }
            var span = spanIndex < line.InlineSpans.Length && line.InlineSpans[spanIndex].Utf16Start <= clip.Utf16Start
                ? line.InlineSpans[spanIndex] : null;
            var ordinary = span?.Style.ApplyTo(line.Style) ?? line.Style;
            var active = edit is null ? null : (clip.ActiveStyle ?? new()).Merge(edit.ToOverride(
                KaraokeVisualStyleResolver.ResolveActive(ordinary, line.KaraokeStyle, clip)));
            if (active != clip.ActiveStyle)
            {
                clips[cursor] = clip with { ActiveStyle = active };
            }
        }
        return WithSubtitleContent(document, index, line with
        {
            Karaoke = line.Karaoke.SequenceEqual(clips) ? line.Karaoke : clips.ToImmutable()
        });
    }
}
