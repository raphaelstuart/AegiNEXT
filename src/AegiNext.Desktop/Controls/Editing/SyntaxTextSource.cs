using Avalonia.Media.TextFormatting;
using Avalonia.Media.TextFormatting.Unicode;
using Avalonia.Utilities;

namespace AegiNext.Desktop.Controls;

internal sealed class SyntaxTextSource(string text, TextRunProperties defaultProperties,
    IReadOnlyList<ValueSpan<TextRunProperties>> styles) : ITextSource
{
    public TextRun? GetTextRun(int textSourceIndex)
    {
        if (textSourceIndex >= text.Length)
        {
            return null;
        }

        var low = 0;
        var high = styles.Count - 1;
        var properties = defaultProperties;
        var length = text.Length - textSourceIndex;
        var matched = false;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var span = styles[middle];
            if (textSourceIndex < span.Start)
            {
                high = middle - 1;
            }
            else if (textSourceIndex >= span.Start + span.Length)
            {
                low = middle + 1;
            }
            else
            {
                properties = span.Value;
                length = span.Start + span.Length - textSourceIndex;
                matched = true;
                break;
            }
        }

        if (!matched && low < styles.Count)
        {
            length = Math.Min(length, Math.Max(1, styles[low].Start - textSourceIndex));
        }

        var graphemes = new GraphemeEnumerator(text.AsSpan(textSourceIndex));
        var completeLength = 0;
        while (graphemes.MoveNext(out var grapheme))
        {
            completeLength += grapheme.Length;
            if (completeLength >= length)
            {
                break;
            }
        }

        return new TextCharacters(text.AsMemory(textSourceIndex, completeLength), properties);
    }
}
