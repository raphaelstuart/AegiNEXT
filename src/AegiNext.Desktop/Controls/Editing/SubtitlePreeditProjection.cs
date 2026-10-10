using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

internal sealed record SubtitlePreeditProjection(SubtitleLine Line, int Start, int Length, int Caret)
{
    internal static SubtitlePreeditProjection Create(SubtitleLine line, int selectionStart, int selectionEnd, string preedit, int cursor)
    {
        var start = Math.Min(selectionStart, selectionEnd);
        var end = Math.Max(selectionStart, selectionEnd);
        var text = line.Text[..start] + preedit + line.Text[end..];
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        var normalizedStart = boundaries.LastOrDefault(value => value <= start);
        var normalizedEnd = boundaries.FirstOrDefault(value => value >= start + preedit.Length, text.Length);
        var caret = boundaries.LastOrDefault(value => value <= start + Math.Clamp(cursor, 0, preedit.Length));
        var spans = ImmutableArray.CreateBuilder<SubtitleInlineSpan>();
        var clips = ImmutableArray.CreateBuilder<KaraokeSegment>();
        var inactiveClips = ImmutableArray.CreateBuilder<KaraokeSegment>();
        var karaokeStyles = ImmutableArray.CreateBuilder<SubtitleKaraokeStyleSpan>();
        var inlineIndex = 0;
        var clipIndex = 0;
        var inactiveIndex = 0;
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var position = boundaries[index];
            var originalPosition = position < start ? position : position >= start + preedit.Length
                ? position - preedit.Length + end - start : Math.Min(start, Math.Max(0, line.Text.Length - 1));
            while (inlineIndex < line.InlineSpans.Length &&
                line.InlineSpans[inlineIndex].Utf16Start + line.InlineSpans[inlineIndex].Utf16Length <= originalPosition)
            {
                inlineIndex++;
            }
            var style = inlineIndex < line.InlineSpans.Length && line.InlineSpans[inlineIndex].Utf16Start <= originalPosition
                ? line.InlineSpans[inlineIndex].Style : null;
            if (style is not null)
            {
                spans.Add(new(position, boundaries[index + 1] - position, style));
            }
            var clip = ClipAt(line.Karaoke, originalPosition, ref clipIndex);
            if (clip is not null)
            {
                AppendClip(clips, clip, position, boundaries[index + 1] - position);
            }
            var inactiveClip = ClipAt(line.InactiveKaraoke, originalPosition, ref inactiveIndex);
            if (inactiveClip is not null)
            {
                AppendClip(inactiveClips, inactiveClip, position, boundaries[index + 1] - position);
            }
            var active = KaraokeVisualStyleResolver.RangeStyleAt(line, originalPosition, KaraokeVisualState.ACTIVE);
            var inactive = KaraokeVisualStyleResolver.RangeStyleAt(line, originalPosition, KaraokeVisualState.INACTIVE);
            if (active is not null || inactive is not null)
            {
                var length = boundaries[index + 1] - position;
                if (karaokeStyles.Count > 0 && karaokeStyles[^1].Utf16Start + karaokeStyles[^1].Utf16Length == position &&
                    karaokeStyles[^1].ActiveStyle == active && karaokeStyles[^1].InactiveStyle == inactive)
                {
                    karaokeStyles[^1] = karaokeStyles[^1] with { Utf16Length = karaokeStyles[^1].Utf16Length + length };
                }
                else
                {
                    karaokeStyles.Add(new(position, length, active, inactive));
                }
            }
        }
        return new(line with
        {
            Text = text, InlineSpans = spans.ToImmutable(), Karaoke = clips.ToImmutable(),
            InactiveKaraoke = inactiveClips.ToImmutable(), KaraokeStyleSpans = karaokeStyles.ToImmutable()
        },
            normalizedStart, normalizedEnd - normalizedStart, caret);
    }

    private static void AppendClip(ImmutableArray<KaraokeSegment>.Builder clips, KaraokeSegment source, int start, int length)
    {
        if (clips.Count > 0 && clips[^1].Id == source.Id && clips[^1].Utf16Start + clips[^1].Utf16Length == start)
        {
            clips[^1] = clips[^1] with { Utf16Length = clips[^1].Utf16Length + length };
        }
        else
        {
            clips.Add(source with { Utf16Start = start, Utf16Length = length });
        }
    }

    private static KaraokeSegment? ClipAt(ImmutableArray<KaraokeSegment> clips, int offset, ref int index)
    {
        while (index < clips.Length && clips[index].Utf16Start + clips[index].Utf16Length <= offset)
        {
            index++;
        }
        return index < clips.Length && clips[index].Utf16Start <= offset ? clips[index] : null;
    }
}
