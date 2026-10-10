using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Styling;
using Avalonia.Media;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleColorTagPaletteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SelectionPreservesTagHueAndIncreasesItsFillStrength(bool dark)
    {
        var row = SubtitleColorTagPalette.ResolveRowBackground("#D43A52", dark, false).Color;
        var selectedRow = SubtitleColorTagPalette.ResolveRowBackground("#D43A52", dark, true).Color;
        var clip = SubtitleColorTagPalette.ResolveClipBackground("#D43A52", dark, false).Color;
        var selectedClip = SubtitleColorTagPalette.ResolveClipBackground("#D43A52", dark, true).Color;

        Assert.Equal((row.R, row.G, row.B), (selectedRow.R, selectedRow.G, selectedRow.B));
        Assert.Equal((clip.R, clip.G, clip.B), (selectedClip.R, selectedClip.G, selectedClip.B));
        Assert.True(selectedRow.A > row.A);
        Assert.True(selectedClip.A > clip.A);
        Assert.InRange(selectedClip.A, 1, 128);
        Assert.Equal(Color.Parse("#D43A52"), SubtitleColorTagPalette.ResolveSwatch("#D43A52").Color);
    }

    [Fact]
    public void AcceptNotifiesTheReusedSubtitleRowWhenItsTagChangesAndUndoRestoresIt()
    {
        var line = new SubtitleLine { Start = new(1), End = new(2), Text = "字幕 ABC" };
        var row = new SubtitleRow(line, 1);
        var changes = new List<string?>();
        row.PropertyChanged += (_, value) => changes.Add(value.PropertyName);
        var tagged = line with { ColorTagId = Guid.NewGuid() };

        row.Accept(tagged);

        Assert.Equal(tagged.ColorTagId, row.ColorTagId);
        Assert.Equal([nameof(SubtitleRow.ColorTagId)], changes);
        Assert.False(row.IsDirty);
        row.Accept(line);
        Assert.Null(row.ColorTagId);
        Assert.Equal(2, changes.Count);
    }
}
