using System.Collections.Immutable;
using System.Numerics;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

/// <summary>将兼容的多字卡拉 OK 片段规范为逐完整 Unicode 字素片段，不裁剪内容时间或修改源快照。</summary>
public static class SubtitleKaraokeNormalization
{
    /// <summary>逐字素等分原片段的精确时间；首字保留原标识，其余新建标识，已规范内容返回原行。</summary>
    public static SubtitleLine Normalize(SubtitleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        ProjectValidator.ValidateText(line.Text);
        if (line.Text.Length > 1000000 || line.Karaoke.IsDefault)
        {
            throw new InvalidDataException("字幕文字或卡拉 OK 数组超过有效范围。");
        }
        if (line.Karaoke.IsEmpty)
        {
            return line;
        }

        var boundaries = SubtitleTextEditMap.Boundaries(line.Text);
        var ids = new HashSet<Guid>();
        var previousEnd = 0;
        foreach (var clip in line.Karaoke.Cast<KaraokeSegment?>())
        {
            if (clip is null || clip.Id == Guid.Empty || !ids.Add(clip.Id) ||
                clip.Utf16Start < previousEnd || clip.Utf16Length <= 0 ||
                (long)clip.Utf16Start + clip.Utf16Length > line.Text.Length ||
                clip.Start < MediaTime.Zero || clip.Start >= clip.End || !Enum.IsDefined(clip.HighlightKind))
            {
                throw new InvalidDataException("卡拉 OK 片段的标识、文字范围或时间无效。");
            }
            previousEnd = checked(clip.Utf16Start + clip.Utf16Length);
            if (Array.BinarySearch(boundaries, clip.Utf16Start) < 0 || Array.BinarySearch(boundaries, previousEnd) < 0)
            {
                throw new InvalidDataException("卡拉 OK 不能拆开完整 Unicode 字素。");
            }
        }

        ImmutableArray<KaraokeSegment>.Builder? changed = null;
        for (var index = 0; index < line.Karaoke.Length; index++)
        {
            var clip = line.Karaoke[index];
            var first = Array.BinarySearch(boundaries, clip.Utf16Start);
            var count = Array.BinarySearch(boundaries, clip.Utf16Start + clip.Utf16Length) - first;
            if (count == 1)
            {
                changed?.Add(clip);
                continue;
            }

            if (changed is null)
            {
                changed = ImmutableArray.CreateBuilder<KaraokeSegment>();
                changed.AddRange(line.Karaoke.AsSpan(0, index));
            }
            var start = clip.Start;
            for (var glyph = 0; glyph < count; glyph++)
            {
                var end = Interpolate(clip.Start, clip.End, glyph + 1, count);
                changed.Add(clip with
                {
                    Id = glyph == 0 ? clip.Id : NewId(ids),
                    Utf16Start = boundaries[first + glyph],
                    Utf16Length = boundaries[first + glyph + 1] - boundaries[first + glyph],
                    Start = start,
                    End = end
                });
                start = end;
            }
        }

        return changed is null ? line : line with { Karaoke = changed.ToImmutable() };
    }

    /// <summary>验证工程并只替换包含旧多字片段的字幕行；图层、裁剪范围及已规范行保留原身份。</summary>
    public static ProjectDocument Normalize(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        ImmutableArray<SubtitleLine>.Builder? changed = null;
        for (var index = 0; index < document.Subtitles.Length; index++)
        {
            var line = document.Subtitles[index];
            var normalized = Normalize(line);
            if (!ReferenceEquals(line, normalized))
            {
                changed ??= document.Subtitles.ToBuilder();
                changed[index] = normalized;
            }
        }
        return changed is null ? document : document with { Subtitles = changed.ToImmutable() };
    }

    private static MediaTime Interpolate(MediaTime start, MediaTime end, int index, int count)
    {
        if (index == count)
        {
            return end;
        }
        var numerator = (BigInteger)start.Numerator * (count - index) * end.Denominator +
            (BigInteger)end.Numerator * index * start.Denominator;
        var denominator = (BigInteger)start.Denominator * end.Denominator * count;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }

    private static Guid NewId(HashSet<Guid> ids)
    {
        Guid id;
        do
        {
            id = Guid.NewGuid();
        }
        while (!ids.Add(id));
        return id;
    }
}
