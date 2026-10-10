using System.Globalization;
using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>在明确的字素边界拆分选中的计时组，其他组与文字外观保持不变。</summary>
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
        var time = splitTime ?? KaraokeTimingMath.Interpolate(clip.Start, clip.End, splitIndex, boundaries.Length - 1);
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

    /// <summary>只将选中的计时组按完整 Unicode 字素精确等分，首字保留原身份。</summary>
    public static ProjectDocument SplitKaraokeClipIntoGraphemes(ProjectDocument document, Guid subtitleId, Guid clipId)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        var clipIndex = KaraokeClipIndex(line, clipId);
        var clip = line.Karaoke[clipIndex];
        var boundaries = ClipBoundaries(line, clip);
        var count = boundaries.Length - 1;
        if (count == 1)
        {
            return document;
        }
        var ids = line.Karaoke.Concat(line.InactiveKaraoke).Select(value => value.Id).ToHashSet();
        var clips = line.Karaoke.ToBuilder();
        clips.RemoveAt(clipIndex);
        for (var glyph = 0; glyph < count; glyph++)
        {
            var id = clip.Id;
            if (glyph > 0)
            {
                do
                {
                    id = Guid.NewGuid();
                }
                while (!ids.Add(id));
            }
            clips.Insert(clipIndex + glyph, clip with
            {
                Id = id, Utf16Start = boundaries[glyph], Utf16Length = boundaries[glyph + 1] - boundaries[glyph],
                Start = KaraokeTimingMath.Interpolate(clip.Start, clip.End, glyph, count),
                End = KaraokeTimingMath.Interpolate(clip.Start, clip.End, glyph + 1, count)
            });
        }
        return WithSubtitleContent(document, index, line with { Karaoke = clips.ToImmutable() });
    }

    /// <summary>合并正文和时间均紧邻、模式相同的计时组，文字范围外观独立保留。</summary>
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
        if (first.HighlightKind != second.HighlightKind)
        {
            throw new InvalidOperationException("不同高亮模式的计时组不能合并，请先统一模式。");
        }
        if (line.KaraokeStyle is null && first.HighlightColor != second.HighlightColor)
        {
            var span = new SubtitleKaraokeStyleSpan(second.Utf16Start, second.Utf16Length,
                new() { Fill = second.HighlightColor });
            line = line with { KaraokeStyleSpans = SubtitleKaraokeStyleEditing.PreserveDefaultFill(line.KaraokeStyleSpans, span) };
        }
        return WithSubtitleContent(document, index, line with
        {
            Karaoke = line.Karaoke.SetItem(firstIndex, first with
            {
                Utf16Length = first.Utf16Length + second.Utf16Length, End = second.End
            }).RemoveAt(secondIndex)
        });
    }

    /// <summary>独立设置计时组的精确起止时间，不移动其他组或改变字幕可见范围。</summary>
    public static ProjectDocument SetKaraokeClipRange(ProjectDocument document, Guid subtitleId, Guid clipId,
        MediaTime start, MediaTime end)
    {
        ProjectValidator.Validate(document);
        ValidateKaraokeRange(start, end);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        var clipIndex = KaraokeClipIndex(line, clipId);
        var clip = line.Karaoke[clipIndex];
        return clip.Start == start && clip.End == end ? document : WithSubtitleContent(document, index, line with
        {
            Karaoke = line.Karaoke.SetItem(clipIndex, clip with { Start = start, End = end })
        });
    }

    /// <summary>只为选中的未计时文字建立原生计时组，起止时间由用户明确提供。</summary>
    public static ProjectDocument CreateKaraokeClip(ProjectDocument document, Guid subtitleId,
        int utf16Start, int utf16Length, MediaTime start, MediaTime end,
        KaraokeHighlightKind highlightKind = KaraokeHighlightKind.SWEEP)
    {
        ProjectValidator.Validate(document);
        ValidateKaraokeRange(start, end);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        SubtitleTextEditMap.ValidateRange(line.Text, SubtitleTextEditMap.Boundaries(line.Text), utf16Start, utf16Length);
        if (utf16Length == 0 || !Enum.IsDefined(highlightKind))
        {
            throw new ArgumentOutOfRangeException(nameof(utf16Length), "请选择完整文字并使用有效高亮模式。");
        }
        if (line.Karaoke.Concat(line.InactiveKaraoke).Any(clip => clip.Utf16Start < utf16Start + utf16Length &&
            clip.Utf16Start + clip.Utf16Length > utf16Start))
        {
            throw new InvalidOperationException("选区已有计时，请编辑或恢复现有计时组。");
        }
        var created = new KaraokeSegment(utf16Start, utf16Length, start, end,
            KaraokeVisualStyleResolver.DefaultHighlightColor) { HighlightKind = highlightKind };
        return WithSubtitleContent(document, index, line with
        {
            Karaoke = line.Karaoke.Add(created).OrderBy(clip => clip.Utf16Start).ToImmutableArray()
        });
    }

    private static void ValidateKaraokeRange(MediaTime start, MediaTime end)
    {
        if (start < MediaTime.Zero || end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "计时组必须具有非负内容起点和正时长。");
        }
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
