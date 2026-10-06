using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using System.Globalization;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleRowTests
{
    [Fact]
    public void UnchangedDisplayedMillisecondsKeepExactThirdsAndOriginalSnapshot()
    {
        var original = CreateLine();
        var row = new SubtitleRow(original, 7);
        Assert.Equal("00:00:00.333", row.StartText);
        Assert.Equal("00:00:00.666", row.EndText);
        Assert.Equal(7, row.Number);
        Assert.Same(original, row.CreateEditedLine(original));

        row.Text = "新文字";
        var edited = row.CreateEditedLine(original);
        Assert.Equal(new MediaTime(1, 3), edited.Start);
        Assert.Equal(new MediaTime(2, 3), edited.End);
        Assert.Equal(original.Id, edited.Id);
        Assert.Same(original.Style, edited.Style);
    }

    [Fact]
    public void EditingOnlyStartKeepsExactEndAndKaraokeContentTimes()
    {
        var original = CreateLine();
        var row = new SubtitleRow(original, 1) { StartText = "0.25" };
        var edited = row.CreateEditedLine(original);
        Assert.Equal(new MediaTime(1, 4), edited.Start);
        Assert.Equal(new MediaTime(2, 3), edited.End);
        Assert.Equal(original.Karaoke, edited.Karaoke);
        Assert.Equal(original.Text, edited.Text);
    }

    [Theory]
    [InlineData("0.75", "0.75")]
    [InlineData("1", "0.5")]
    public void InvalidIntervalCannotProduceAReplacement(string start, string end)
    {
        var original = CreateLine();
        var row = new SubtitleRow(original, 1) { StartText = start, EndText = end };
        Assert.Throws<InvalidDataException>(() => row.CreateEditedLine(original));
        Assert.Equal(new MediaTime(1, 3), original.Start);
        Assert.Equal(new MediaTime(2, 3), original.End);
    }

    [Fact]
    public void DraftCannotBeAppliedToAnotherStableSubtitleId()
    {
        var original = CreateLine();
        var row = new SubtitleRow(original, 1);
        var different = original with { Id = Guid.NewGuid() };
        Assert.Throws<ArgumentException>(() => row.CreateEditedLine(different));
    }

    [Fact]
    public void UnicodeTextEditRedistributesAffectedKaraokeWithoutSplittingGraphemes()
    {
        var original = CreateLine();
        const string REPLACEMENT = "日本語 👨‍👩‍👧‍👦\ne\u0301";
        var row = new SubtitleRow(original, 1) { Text = REPLACEMENT };
        var edited = row.CreateEditedLine(original);
        Assert.Equal(REPLACEMENT, edited.Text);
        Assert.NotEmpty(edited.Karaoke);
        var boundaries = StringInfo.ParseCombiningCharacters(REPLACEMENT).Append(REPLACEMENT.Length).ToHashSet();
        Assert.All(edited.Karaoke, clip =>
        {
            Assert.Contains(clip.Utf16Start, boundaries);
            Assert.Contains(clip.Utf16Start + clip.Utf16Length, boundaries);
        });
        Assert.Equal(original.Karaoke[0].Start, edited.Karaoke[0].Start);
        Assert.Equal(original.Karaoke[^1].End, edited.Karaoke[^1].End);
        Assert.NotEmpty(original.Karaoke);
        Assert.Equal("A😀e\u0301", original.Text);
    }

    [Fact]
    public void EqualPropertyValueDoesNotRaiseAnotherBindingNotification()
    {
        var row = new SubtitleRow(CreateLine(), 1);
        var changes = new List<string?>();
        row.PropertyChanged += (_, change) => changes.Add(change.PropertyName);
        row.Text = row.Text;
        row.StartText = row.StartText;
        row.EndText = row.EndText;
        Assert.Empty(changes);
        row.Text = "changed";
        Assert.Equal(nameof(SubtitleRow.Text), Assert.Single(changes));
    }

    private static SubtitleLine CreateLine()
    {
        return new()
        {
            Start = new(1, 3), End = new(2, 3), Text = "A😀e\u0301",
            Karaoke = [new(0, 1, new(0), new(1, 6), SceneColor.White), new(1, 2, new(1, 6), new(1, 3), SceneColor.White)]
        };
    }
}
