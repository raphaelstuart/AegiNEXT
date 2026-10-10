using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitlePreeditProjectionTests
{
    [Fact]
    public void CompositionKeepsWholeGroupClocksAndRebasesVisualRanges()
    {
        var clip = new KaraokeSegment(0, 4, new(1, 3), new(7, 3), SceneColor.White);
        var line = new SubtitleLine
        {
            Text = "abcd", Karaoke = [clip],
            KaraokeStyleSpans = [new(2, 2, new() { StrokeWidth = 8 }, new() { ShadowOffset = new(10, 0) })]
        };
        var result = SubtitlePreeditProjection.Create(line, 1, 1, "😀", 1).Line;
        var projected = Assert.Single(result.Karaoke);
        Assert.Equal(clip.Id, projected.Id);
        Assert.Equal(clip.Start, projected.Start);
        Assert.Equal(clip.End, projected.End);
        Assert.Equal(6, projected.Utf16Length);
        Assert.Equal(line.KaraokeStyleSpans[0] with { Utf16Start = 4 }, Assert.Single(result.KaraokeStyleSpans));
        ProjectValidator.ValidateSubtitleKaraoke(result);
        Assert.Same(clip, Assert.Single(line.Karaoke));
    }

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
