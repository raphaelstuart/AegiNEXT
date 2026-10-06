using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal static class SubtitleContentEditing
{
    private static readonly SubtitleInlineStyleOverride emptyStyle = new();

    internal static SubtitleLine ReplaceText(SubtitleLine line, int start, int length, string replacement)
    {
        line = SubtitleKaraokeNormalization.Normalize(line);
        var map = new SubtitleTextEditMap(line.Text, start, length, replacement);
        if (map.Text == line.Text)
        {
            return line;
        }
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        for (var index = 0; index < map.NewBoundaries.Length - 1; index++)
        {
            var offset = map.NewBoundaries[index];
            AppendSpan(spans, offset, map.NewBoundaries[index + 1] - offset,
                StyleAt(line.InlineSpans, map.StyleSourceOffset(offset)));
        }
        var (karaoke, inactiveKaraoke) = RemapKaraoke(line, map);
        return line with
        {
            Text = map.Text,
            InlineSpans = Reuse(line.InlineSpans, spans.ToImmutable()),
            Karaoke = karaoke,
            InactiveKaraoke = inactiveKaraoke
        };
    }

    internal static SubtitleLine ApplyInlineStyle(SubtitleLine line, int start, int length,
        SubtitleInlineStyleOverride? overlay)
    {
        var boundaries = SubtitleTextEditMap.Boundaries(line.Text);
        SubtitleTextEditMap.ValidateRange(line.Text, boundaries, start, length);
        if (length == 0 || overlay is { HasOverrides: false })
        {
            return line;
        }
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var offset = boundaries[index];
            var style = StyleAt(line.InlineSpans, offset);
            if (offset >= start && offset < start + length)
            {
                style = overlay is null ? emptyStyle : style.Merge(overlay);
            }
            AppendSpan(spans, offset, boundaries[index + 1] - offset, style);
        }
        var next = Reuse(line.InlineSpans, spans.ToImmutable());
        return next == line.InlineSpans ? line : line with { InlineSpans = next };
    }

    internal static (ImmutableArray<KaraokeSegment> Karaoke, ImmutableArray<KaraokeSegment> InactiveKaraoke) RemapKaraoke(
        SubtitleLine line, SubtitleTextEditMap map)
    {
        if (line.InactiveKaraoke.IsEmpty)
        {
            return (RemapKaraoke(line.Karaoke, line.Text.Length, map), line.InactiveKaraoke);
        }
        if (line.Karaoke.IsEmpty)
        {
            return (line.Karaoke, RemapKaraoke(line.InactiveKaraoke, line.Text.Length, map));
        }
        var inactiveIds = line.InactiveKaraoke.Select(clip => clip.Id).ToHashSet();
        var clips = line.Karaoke.AddRange(line.InactiveKaraoke).OrderBy(clip => clip.Utf16Start).ToImmutableArray();
        var remapped = RemapKaraoke(clips, line.Text.Length, map, inactiveIds);
        return (
            Reuse(line.Karaoke, remapped.Where(clip => !inactiveIds.Contains(clip.Id)).ToImmutableArray()),
            Reuse(line.InactiveKaraoke, remapped.Where(clip => inactiveIds.Contains(clip.Id)).ToImmutableArray()));
    }

    private static ImmutableArray<KaraokeSegment> RemapKaraoke(ImmutableArray<KaraokeSegment> clips,
        int textLength, SubtitleTextEditMap map, HashSet<Guid>? inactiveIds = null)
    {
        if (clips.IsEmpty || map.Text.Length == 0)
        {
            return [];
        }
        if (map.OldCount == map.NewCount)
        {
            return Reuse(clips, clips.Select(segment => MapSegment(segment, map)).ToImmutableArray());
        }
        var first = -1;
        var last = -1;
        for (var index = 0; index < clips.Length; index++)
        {
            var segment = clips[index];
            var segmentEnd = segment.Utf16Start + segment.Utf16Length;
            var touches = map.OldStart == map.OldEnd
                ? segment.Utf16Start <= map.OldStart && (map.OldStart < segmentEnd ||
                    map.OldStart == textLength && segmentEnd == map.OldStart)
                : segment.Utf16Start < map.OldEnd && segmentEnd > map.OldStart;
            if (touches)
            {
                first = first < 0 ? index : first;
                last = index;
            }
        }
        if (first < 0)
        {
            return Reuse(clips, clips.Select(segment => MapSegment(segment, map)).ToImmutableArray());
        }
        for (var index = Math.Max(first, 1); index <= Math.Min(last + 1, clips.Length - 1); index++)
        {
            if (clips[index].Start < clips[index - 1].End)
            {
                throw new InvalidOperationException("重叠或逆序的旧字时间必须先明确调整，不能自动重分配。");
            }
        }

        var left = clips[first];
        var right = clips[last];
        var regionStart = map.MapBoundary(Math.Min(left.Utf16Start, map.OldStart));
        var regionEnd = Math.Max(right.Utf16Start + right.Utf16Length, map.OldEnd) + map.Delta;
        var firstGlyph = Array.BinarySearch(map.NewBoundaries, regionStart);
        var glyphCount = Array.BinarySearch(map.NewBoundaries, regionEnd) - firstGlyph;
        var result = ImmutableArray.CreateBuilder<KaraokeSegment>();
        for (var index = 0; index < first; index++)
        {
            result.Add(MapSegment(clips[index], map));
        }
        if (glyphCount > 0)
        {
            var groups = TimingGroups(clips, map.OldBoundaries, first, last);
            var counts = AllocateGlyphs(groups, glyphCount);
            var usedIds = new HashSet<Guid>();
            var glyph = 0;
            for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
            {
                var group = groups[groupIndex];
                var groupStart = clips[group.First].Start;
                var duration = clips[group.Last].End - groupStart;
                var count = counts[groupIndex];
                for (var index = 0; index < count; index++, glyph++)
                {
                    var offset = map.NewBoundaries[firstGlyph + glyph];
                    var source = map.StyleSourceOffset(offset);
                    var templateIndex = SegmentAt(clips, source);
                    var template = clips[Math.Clamp(templateIndex, group.First, group.Last)];
                    var id = usedIds.Add(template.Id) ? template.Id : Guid.NewGuid();
                    if (inactiveIds?.Contains(template.Id) == true)
                    {
                        inactiveIds.Add(id);
                    }
                    result.Add(template with
                    {
                        Id = id,
                        Utf16Start = offset,
                        Utf16Length = map.NewBoundaries[firstGlyph + glyph + 1] - offset,
                        Start = groupStart + duration / count * index,
                        End = groupStart + duration / count * (index + 1)
                    });
                }
            }
        }
        for (var index = last + 1; index < clips.Length; index++)
        {
            result.Add(MapSegment(clips[index], map));
        }
        if (glyphCount == 0 && result.Count > 0)
        {
            var released = MediaTime.Zero;
            for (var index = first; index <= last; index++)
            {
                released += clips[index].End - clips[index].Start;
            }
            if (first < result.Count)
            {
                result[first] = result[first] with { Start = result[first].Start - released };
            }
            else
            {
                result[^1] = result[^1] with { End = result[^1].End + released };
            }
        }
        return Reuse(clips, result.ToImmutable());
    }

    private static List<(int First, int Last, int Weight)> TimingGroups(ImmutableArray<KaraokeSegment> segments,
        int[] boundaries, int first, int last)
    {
        List<(int First, int Last, int Weight)> groups = [];
        for (var index = first; index <= last; index++)
        {
            var segment = segments[index];
            var count = Array.BinarySearch(boundaries, segment.Utf16Start + segment.Utf16Length) -
                Array.BinarySearch(boundaries, segment.Utf16Start);
            if (groups.Count > 0 && segment.Start == segments[index - 1].End)
            {
                var previous = groups[^1];
                groups[^1] = (previous.First, index, previous.Weight + count);
            }
            else
            {
                groups.Add((index, index, count));
            }
        }
        return groups;
    }

    private static int[] AllocateGlyphs(List<(int First, int Last, int Weight)> groups, int count)
    {
        if (count < groups.Count)
        {
            throw new InvalidOperationException("替换后的字数不足以保留原有等待区间，请先明确调整字时间。");
        }
        var counts = new int[groups.Count];
        var remaining = count - groups.Count;
        var totalWeight = groups.Sum(group => group.Weight);
        List<(int Index, long Remainder)> remainders = [];
        for (var index = 0; index < groups.Count; index++)
        {
            var weighted = (long)remaining * groups[index].Weight;
            counts[index] = 1 + (int)(weighted / totalWeight);
            remainders.Add((index, weighted % totalWeight));
        }
        var extra = count - counts.Sum();
        foreach (var remainder in remainders.OrderByDescending(value => value.Remainder).ThenBy(value => value.Index).Take(extra))
        {
            counts[remainder.Index]++;
        }
        return counts;
    }

    private static KaraokeSegment MapSegment(KaraokeSegment segment, SubtitleTextEditMap map)
    {
        var start = map.MapBoundary(segment.Utf16Start);
        var end = map.MapBoundary(segment.Utf16Start + segment.Utf16Length);
        return start == segment.Utf16Start && end - start == segment.Utf16Length
            ? segment : segment with { Utf16Start = start, Utf16Length = end - start };
    }

    private static SubtitleInlineStyleOverride StyleAt(ImmutableArray<SubtitleInlineSpan> spans, int offset)
    {
        var low = 0;
        var high = spans.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var span = spans[middle];
            if (offset < span.Utf16Start)
            {
                high = middle - 1;
            }
            else if (offset >= span.Utf16Start + span.Utf16Length)
            {
                low = middle + 1;
            }
            else
            {
                return span.Style;
            }
        }
        return emptyStyle;
    }

    private static int SegmentAt(ImmutableArray<KaraokeSegment> segments, int offset)
    {
        var low = 0;
        var high = segments.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var segment = segments[middle];
            if (offset < segment.Utf16Start)
            {
                high = middle - 1;
            }
            else if (offset >= segment.Utf16Start + segment.Utf16Length)
            {
                low = middle + 1;
            }
            else
            {
                return middle;
            }
        }
        return low;
    }

    private static void AppendSpan(ImmutableArray<SubtitleInlineSpan>.Builder spans, int start, int length,
        SubtitleInlineStyleOverride style)
    {
        if (!style.HasOverrides)
        {
            return;
        }
        if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == start && spans[^1].Style == style)
        {
            spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + length };
        }
        else
        {
            spans.Add(new(start, length, style));
        }
    }

    private static ImmutableArray<T> Reuse<T>(ImmutableArray<T> original, ImmutableArray<T> next)
    {
        return original.SequenceEqual(next) ? original : next;
    }
}
