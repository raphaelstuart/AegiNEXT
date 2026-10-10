using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleKaraokeStyleEditing
{
    internal static SubtitleLine Edit(SubtitleLine line, int start, int length, KaraokeVisualState state,
        KaraokeVisualStyleEdit? edit)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        var boundaries = SubtitleTextEditMap.Boundaries(line.Text);
        SubtitleTextEditMap.ValidateRange(line.Text, boundaries, start, length);
        if (length == 0 || edit is { HasChanges: false })
        {
            return line;
        }
        var spans = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var offset = boundaries[index];
            var active = KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.ACTIVE);
            var inactive = KaraokeVisualStyleResolver.RangeStyleAt(line, offset, KaraokeVisualState.INACTIVE);
            if (offset >= start && offset < start + length)
            {
                var existing = state == KaraokeVisualState.ACTIVE ? active : inactive;
                var next = edit is null ? null : (existing ?? new()).Merge(edit.ToOverride(Resolve(line, offset, state)));
                if (state == KaraokeVisualState.ACTIVE)
                {
                    active = next;
                }
                else
                {
                    inactive = next;
                }
            }
            Append(spans, new(offset, boundaries[index + 1] - offset, active, inactive));
        }
        var result = Reuse(line.KaraokeStyleSpans, spans.ToImmutable());
        return result == line.KaraokeStyleSpans ? line : line with { KaraokeStyleSpans = result };
    }

    internal static ImmutableArray<SubtitleKaraokeStyleSpan> Remap(ImmutableArray<SubtitleKaraokeStyleSpan> original,
        SubtitleTextEditMap map)
    {
        if (original.IsEmpty)
        {
            return original;
        }
        var spans = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        for (var index = 0; index < map.NewBoundaries.Length - 1; index++)
        {
            var offset = map.NewBoundaries[index];
            var source = map.StyleSourceOffset(offset);
            Append(spans, new(offset, map.NewBoundaries[index + 1] - offset,
                KaraokeVisualStyleResolver.StyleAt(original, source, KaraokeVisualState.ACTIVE),
                KaraokeVisualStyleResolver.StyleAt(original, source, KaraokeVisualState.INACTIVE)));
        }
        return Reuse(original, spans.ToImmutable());
    }

    internal static ImmutableArray<SubtitleKaraokeStyleSpan> PreserveDefaultFill(
        ImmutableArray<SubtitleKaraokeStyleSpan> spans, SubtitleKaraokeStyleSpan defaultSpan)
    {
        var boundaries = spans.SelectMany(span => new[] { span.Utf16Start, span.Utf16Start + span.Utf16Length })
            .Append(defaultSpan.Utf16Start).Append(defaultSpan.Utf16Start + defaultSpan.Utf16Length)
            .Distinct().Order().ToArray();
        var result = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var start = boundaries[index];
            var active = KaraokeVisualStyleResolver.StyleAt(spans, start, KaraokeVisualState.ACTIVE);
            if (start >= defaultSpan.Utf16Start && start < defaultSpan.Utf16Start + defaultSpan.Utf16Length)
            {
                active = (active ?? new()) with { Fill = active?.Fill ?? defaultSpan.ActiveStyle?.Fill };
            }
            Append(result, new(start, boundaries[index + 1] - start, active,
                KaraokeVisualStyleResolver.StyleAt(spans, start, KaraokeVisualState.INACTIVE)));
        }
        return Reuse(spans, result.ToImmutable());
    }

    internal static SubtitleStyle OrdinaryAt(SubtitleLine line, int offset)
    {
        var low = 0;
        var high = line.InlineSpans.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var span = line.InlineSpans[middle];
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
                return span.Style.ApplyTo(line.Style);
            }
        }
        return line.Style;
    }

    internal static KaraokeSegment? SegmentAt(SubtitleLine line, int offset)
    {
        return FindSegment(line.Karaoke, offset) ?? FindSegment(line.InactiveKaraoke, offset);
    }

    internal static void Append(ImmutableArray<SubtitleKaraokeStyleSpan>.Builder spans, SubtitleKaraokeStyleSpan span)
    {
        span = span with
        {
            ActiveStyle = span.ActiveStyle is { HasOverrides: true } ? span.ActiveStyle : null,
            InactiveStyle = span.InactiveStyle is { HasOverrides: true } ? span.InactiveStyle : null
        };
        if (span.Utf16Length == 0 || span.ActiveStyle is null && span.InactiveStyle is null)
        {
            return;
        }
        if (spans.Count > 0 && spans[^1].Utf16Start + spans[^1].Utf16Length == span.Utf16Start &&
            spans[^1].ActiveStyle == span.ActiveStyle && spans[^1].InactiveStyle == span.InactiveStyle)
        {
            spans[^1] = spans[^1] with { Utf16Length = spans[^1].Utf16Length + span.Utf16Length };
        }
        else
        {
            spans.Add(span);
        }
    }

    private static SubtitleStyle Resolve(SubtitleLine line, int offset, KaraokeVisualState state)
    {
        var ordinary = OrdinaryAt(line, offset);
        var segment = SegmentAt(line, offset);
        var rangeStyle = KaraokeVisualStyleResolver.RangeStyleAt(line, offset, state);
        return state == KaraokeVisualState.ACTIVE
            ? KaraokeVisualStyleResolver.ResolveActive(ordinary, line.KaraokeStyle, segment, rangeStyle)
            : KaraokeVisualStyleResolver.ResolveInactive(ordinary, segment, rangeStyle);
    }

    private static KaraokeSegment? FindSegment(ImmutableArray<KaraokeSegment> segments, int offset)
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
                return segment;
            }
        }
        return null;
    }

    private static ImmutableArray<SubtitleKaraokeStyleSpan> Reuse(ImmutableArray<SubtitleKaraokeStyleSpan> original,
        ImmutableArray<SubtitleKaraokeStyleSpan> next)
    {
        return original.SequenceEqual(next) ? original : next;
    }
}
