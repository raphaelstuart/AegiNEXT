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
        for (var index = 0; index < boundaries.Length - 1; index++)
        {
            var position = boundaries[index];
            var originalPosition = position < start ? position : position >= start + preedit.Length
                ? position - preedit.Length + end - start : Math.Min(start, Math.Max(0, line.Text.Length - 1));
            var style = line.InlineSpans.FirstOrDefault(span => span.Utf16Start <= originalPosition &&
                span.Utf16Start + span.Utf16Length > originalPosition)?.Style;
            if (style is not null)
            {
                spans.Add(new(position, boundaries[index + 1] - position, style));
            }
            var clip = line.Karaoke.FirstOrDefault(value => value.Utf16Start <= originalPosition &&
                value.Utf16Start + value.Utf16Length > originalPosition);
            if (clip is not null)
            {
                clips.Add(clip with { Utf16Start = position, Utf16Length = boundaries[index + 1] - position });
            }
        }
        return new(line with { Text = text, InlineSpans = spans.ToImmutable(), Karaoke = clips.ToImmutable() },
            normalizedStart, normalizedEnd - normalizedStart, caret);
    }
}
