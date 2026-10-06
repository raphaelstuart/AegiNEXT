using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitlePreeditProjectionTests
{
    [Theory]
    [InlineData("ab", 1, 1, "\u0301", 1, "a\u0301b")]
    [InlineData("ab", 1, 1, "👩‍💻", 1, "a👩‍💻b")]
    [InlineData("ab", 0, 2, "中文", 1, "中文")]
    [InlineData("👩💻", 2, 2, "\u200D", 1, "👩‍💻")]
    public void PreeditReplacesSelectionAndNormalizesAllGeometryToFinalGraphemes(string text, int start, int end,
        string preedit, int cursor, string expected)
    {
        var line = new SubtitleLine { Text = text, InlineSpans = [new(0, text.Length, new() { Bold = true })] };
        var result = SubtitlePreeditProjection.Create(line, start, end, preedit, cursor);
        Assert.Equal(expected, result.Line.Text);
        Assert.Equal(text, line.Text);
        var boundaries = StringInfo.ParseCombiningCharacters(expected).Append(expected.Length).ToHashSet();
        Assert.Contains(result.Caret, boundaries);
        Assert.Contains(result.Start, boundaries);
        Assert.Contains(result.Start + result.Length, boundaries);
        Assert.All(result.Line.InlineSpans, span =>
        {
            Assert.Contains(span.Utf16Start, boundaries);
            Assert.Contains(span.Utf16Start + span.Utf16Length, boundaries);
            Assert.True(span.Style.Bold);
        });
    }
}
