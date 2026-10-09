using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleContentSplitMerge
{
    internal static ImmutableArray<SubtitleInlineSpan> SplitSpans(ImmutableArray<SubtitleInlineSpan> spans, int offset, bool right)
    {
        var result = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        foreach (var span in spans)
        {
            var start = right ? Math.Max(offset, span.Utf16Start) : span.Utf16Start;
            var end = right ? span.Utf16Start + span.Utf16Length : Math.Min(offset, span.Utf16Start + span.Utf16Length);
            if (end > start)
            {
                result.Add(new(right ? start - offset : start, end - start, span.Style));
            }
        }
        return result.ToImmutable();
    }

    internal static ImmutableArray<SubtitleInlineSpan> MergeSpans(SubtitleLine first, SubtitleLine second, int offset)
    {
        var result = first.InlineSpans.ToBuilder();
        if (first.Style == second.Style)
        {
            foreach (var span in second.InlineSpans)
            {
                Append(result, span with { Utf16Start = span.Utf16Start + offset });
            }
            return result.ToImmutable();
        }
        var cursor = 0;
        foreach (var span in second.InlineSpans)
        {
            if (span.Utf16Start > cursor)
            {
                Append(result, new(offset + cursor, span.Utf16Start - cursor, SubtitleInlineStyleOverride.FromStyle(second.Style)));
            }
            Append(result, new(offset + span.Utf16Start, span.Utf16Length,
                SubtitleInlineStyleOverride.FromStyle(span.Style.ApplyTo(second.Style))));
            cursor = span.Utf16Start + span.Utf16Length;
        }
        if (cursor < second.Text.Length)
        {
            Append(result, new(offset + cursor, second.Text.Length - cursor, SubtitleInlineStyleOverride.FromStyle(second.Style)));
        }
        return result.ToImmutable();
    }

    internal static KaraokeVisualStyleOverride PreserveHighlight(SubtitleLine line, KaraokeSegment segment)
    {
        var style = line.KaraokeStyle;
        var active = segment.ActiveStyle;
        return new()
        {
            Fill = active?.Fill ?? style?.Fill ?? segment.HighlightColor,
            FillBlur = active?.FillBlur ?? style?.FillBlur,
            Stroke = active?.Stroke ?? style?.Stroke,
            StrokeBlur = active?.StrokeBlur ?? style?.StrokeBlur,
            StrokeWidth = active?.StrokeWidth ?? style?.StrokeWidth,
            ShadowOffset = active?.ShadowOffset ?? style?.ShadowOffset,
            ShadowBlur = active?.ShadowBlur ?? style?.ShadowBlur,
            ShadowColor = active?.ShadowColor ?? style?.ShadowColor
        };
    }

    private static void Append(ImmutableArray<SubtitleInlineSpan>.Builder result, SubtitleInlineSpan span)
    {
        if (result.Count > 0 && result[^1].Utf16Start + result[^1].Utf16Length == span.Utf16Start && result[^1].Style == span.Style)
        {
            result[^1] = result[^1] with { Utf16Length = result[^1].Utf16Length + span.Utf16Length };
        }
        else
        {
            result.Add(span);
        }
    }
}
