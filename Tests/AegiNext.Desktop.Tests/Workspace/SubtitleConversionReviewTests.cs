using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class SubtitleConversionReviewTests
{
    [Fact]
    public void DetailsGroupNotesUnderReadableSubtitlePositionsAndKeepGlobalNotes()
    {
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "First 中文\nsecond line" };
        var second = new SubtitleLine { Start = new(4), End = new(6), Text = "Second" };
        var review = new SubtitleConversionReview(
        [
            new("Ass.First", "First note", SubtitleId: first.Id),
            new("Ass.Other", "Other subtitle", SubtitleId: second.Id),
            new("Ass.Second", "Second note", SubtitleId: first.Id),
            new("Ass.Global", "Global note")
        ], [first, second]);

        var details = review.FormatDetails();

        Assert.Contains("00:00:01.000 → 00:00:03.000", details);
        Assert.Contains("First 中文 second line", details);
        Assert.DoesNotContain(first.Id.ToString(), details);
        Assert.DoesNotContain(second.Id.ToString(), details);
        Assert.True(details.IndexOf("Second note", StringComparison.Ordinal) < details.IndexOf("Other subtitle", StringComparison.Ordinal));
        Assert.Contains("Ass.Global: Global note", details);
        Assert.Equal(2, review.AffectedSubtitleCount);
    }

    [Fact]
    public void PreviewBoundsLargeContentWithoutSplittingGraphemesOrChangingTheSubtitle()
    {
        var line = new SubtitleLine { Text = string.Concat(Enumerable.Repeat("👩‍👩‍👧‍👦", 500)) };
        var review = new SubtitleConversionReview([new("Ass.Note", "Note", SubtitleId: line.Id)], [line]);

        var details = review.FormatDetails();

        Assert.Contains(string.Concat(Enumerable.Repeat("👩‍👩‍👧‍👦", 120)) + "…", details);
        Assert.DoesNotContain(string.Concat(Enumerable.Repeat("👩‍👩‍👧‍👦", 121)), details);
        Assert.Same(line, Assert.Single(review.Subtitles));
        Assert.Equal(string.Concat(Enumerable.Repeat("👩‍👩‍👧‍👦", 500)), line.Text);
    }
}
