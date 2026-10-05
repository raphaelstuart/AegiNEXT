using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>兼容拆分旧多字片段，并规范化为逐字素输出；已逐字的片段没有可拆分的内部边界。</summary>
    public static ProjectDocument SplitKaraokeClip(ProjectDocument document, Guid subtitleId, Guid clipId,
        int utf16Offset, MediaTime? splitTime = null)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        var clipIndex = KaraokeClipIndex(line, clipId);
        var clip = line.Karaoke[clipIndex];
        var boundaries = ClipBoundaries(line, clip);
        var splitIndex = Array.IndexOf(boundaries, utf16Offset);
        if (splitIndex <= 0 || splitIndex >= boundaries.Length - 1)
        {
            throw new ArgumentOutOfRangeException(nameof(utf16Offset), "拆分必须位于片段内部的完整字素边界。");
        }
        var time = splitTime ?? clip.Start + (clip.End - clip.Start) / (boundaries.Length - 1) * splitIndex;
        if (time <= clip.Start || time >= clip.End)
        {
            throw new ArgumentOutOfRangeException(nameof(splitTime), "拆分时刻必须位于片段时间内部。");
        }
        var left = clip with { Utf16Length = utf16Offset - clip.Utf16Start, End = time };
        var right = clip with
        {
            Id = Guid.NewGuid(), Utf16Start = utf16Offset,
            Utf16Length = clip.Utf16Start + clip.Utf16Length - utf16Offset, Start = time
        };
        return WithSubtitleContent(document, index, line with
        {
            Karaoke = line.Karaoke.SetItem(clipIndex, left).Insert(clipIndex + 1, right)
        });
    }

    /// <summary>兼容旧拆分入口，使用共享规范化为整行生成逐字素片段；已规范行不产生事务。</summary>
    public static ProjectDocument SplitKaraokeClipIntoGraphemes(ProjectDocument document, Guid subtitleId, Guid clipId)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        KaraokeClipIndex(line, clipId);
        return WithSubtitleContent(document, index, line);
    }

    /// <summary>兼容重分配两个紧邻同样式片段的总时长；输出仍为逐字素，首字保留第一个身份。</summary>
    public static ProjectDocument MergeKaraokeClips(ProjectDocument document, Guid subtitleId, Guid firstId, Guid secondId)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        var firstIndex = KaraokeClipIndex(line, firstId);
        var secondIndex = KaraokeClipIndex(line, secondId);
        var first = line.Karaoke[firstIndex];
        var second = line.Karaoke[secondIndex];
        if (secondIndex != firstIndex + 1 || first.Utf16Start + first.Utf16Length != second.Utf16Start || first.End != second.Start)
        {
            throw new InvalidOperationException("只能合并正文与时间均紧邻的片段；等待区或重叠时间必须保留。");
        }
        if (first.HighlightKind != second.HighlightKind || first.HighlightColor != second.HighlightColor ||
            first.InactiveStyle != second.InactiveStyle || first.ActiveStyle != second.ActiveStyle)
        {
            throw new InvalidOperationException("不同高亮配置的片段不能合并，请先统一片段配置。");
        }
        return WithSubtitleContent(document, index, line with
        {
            Karaoke = line.Karaoke.SetItem(firstIndex, first with
            {
                Utf16Length = first.Utf16Length + second.Utf16Length, End = second.End
            }).RemoveAt(secondIndex)
        });
    }

    private static int KaraokeClipIndex(SubtitleLine line, Guid id)
    {
        for (var index = 0; index < line.Karaoke.Length; index++)
        {
            if (line.Karaoke[index].Id == id)
            {
                return index;
            }
        }
        throw new KeyNotFoundException("卡拉 OK 片段不存在。");
    }

    private static int[] ClipBoundaries(SubtitleLine line, KaraokeSegment clip)
    {
        return StringInfo.ParseCombiningCharacters(line.Text).Append(line.Text.Length)
            .Where(offset => offset >= clip.Utf16Start && offset <= clip.Utf16Start + clip.Utf16Length).ToArray();
    }
}
