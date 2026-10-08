using System.Collections.Immutable;
using System.Numerics;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

/// <summary>将启用与禁用的多字高亮片段规范为逐完整 Unicode 字素片段，不裁剪内容时间或修改源快照。</summary>
public static class SubtitleKaraokeNormalization
{
    /// <summary>逐字素等分启用与禁用片段的精确时间；首字保留原标识，未改变的数组及已规范行保留原身份。</summary>
    public static SubtitleLine Normalize(SubtitleLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Karaoke.IsDefaultOrEmpty && line.InactiveKaraoke.IsDefaultOrEmpty)
        {
            ProjectValidator.ValidateSubtitleKaraoke(line);
            return line;
        }
        if (line.Text is null)
        {
            throw new InvalidDataException("字幕文字不能为 null。");
        }
        var boundaries = new SubtitleTextBoundaries(line.Text);
        ProjectValidator.ValidateSubtitleKaraoke(line, boundaries);
        return NormalizeValidated(line, boundaries);
    }

    private static SubtitleLine NormalizeValidated(SubtitleLine line, SubtitleTextBoundaries? boundaries = null)
    {
        if (line.Karaoke.IsEmpty && line.InactiveKaraoke.IsEmpty)
        {
            return line;
        }

        boundaries ??= new(line.Text);
        var ids = line.Karaoke.Concat(line.InactiveKaraoke).Select(clip => clip.Id).ToHashSet();
        var karaoke = NormalizeSegments(line.Karaoke, boundaries, ids);
        var inactiveKaraoke = NormalizeSegments(line.InactiveKaraoke, boundaries, ids);
        return karaoke == line.Karaoke && inactiveKaraoke == line.InactiveKaraoke
            ? line : line with { Karaoke = karaoke, InactiveKaraoke = inactiveKaraoke };
    }

    private static ImmutableArray<KaraokeSegment> NormalizeSegments(ImmutableArray<KaraokeSegment> segments,
        SubtitleTextBoundaries boundaries, HashSet<Guid> ids)
    {
        ImmutableArray<KaraokeSegment>.Builder? changed = null;
        for (var index = 0; index < segments.Length; index++)
        {
            var clip = segments[index];
            var first = boundaries.IndexOf(clip.Utf16Start);
            var count = boundaries.IndexOf(clip.Utf16Start + clip.Utf16Length) - first;
            if (count == 1)
            {
                changed?.Add(clip);
                continue;
            }

            if (changed is null)
            {
                changed = ImmutableArray.CreateBuilder<KaraokeSegment>();
                changed.AddRange(segments.AsSpan(0, index));
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

        return changed is null ? segments : changed.ToImmutable();
    }

    /// <summary>验证工程并只替换包含旧多字片段的字幕行；图层、裁剪范围及已规范行保留原身份。</summary>
    public static ProjectDocument Normalize(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        ImmutableArray<SubtitleLine>.Builder? changed = null;
        for (var index = 0; index < document.Subtitles.Length; index++)
        {
            var line = document.Subtitles[index];
            var normalized = NormalizeValidated(line);
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
