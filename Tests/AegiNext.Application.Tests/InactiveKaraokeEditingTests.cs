using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class InactiveKaraokeEditingTests
{
    private static readonly int[] graphemeStarts = [0, 1, 3];
    private static readonly int[] graphemeLengths = [1, 2, 5];

    [Fact]
    public void MixedLineCanDisableAndRestoreAllClipsWithoutLosingExistingInactiveData()
    {
        var document = Document(true);
        var line = document.Subtitles[0];
        var allClips = line.Karaoke.AddRange(line.InactiveKaraoke);
        var editor = new ProjectEditor(document);
        editor.SetSubtitleKaraokeEnabled(line.Id, false);
        var disabled = editor.Snapshot;
        Assert.Empty(disabled.Subtitles[0].Karaoke);
        Assert.Equal(allClips, disabled.Subtitles[0].InactiveKaraoke);
        Assert.Equal(line.KaraokeStyle, disabled.Subtitles[0].KaraokeStyle);
        editor.SetSubtitleKaraokeEnabled(line.Id, false);
        Assert.Same(disabled, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        editor.SetSubtitleKaraokeEnabled(line.Id, true);
        var restored = editor.Snapshot;
        Assert.Equal(allClips, restored.Subtitles[0].Karaoke);
        Assert.Empty(restored.Subtitles[0].InactiveKaraoke);
        Assert.Equal(line.KaraokeStyle, restored.Subtitles[0].KaraokeStyle);
        editor.SetSubtitleKaraokeEnabled(line.Id, true);
        Assert.Same(restored, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(disabled, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(restored, editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InsertionRemapsSavedClipsAndKeepsTheirPreciseTimesAndState(bool mixed)
    {
        var document = Document(mixed);
        var line = document.Subtitles[0];
        var source = line.InactiveKaraoke.Single(clip => clip.Utf16Start == 1);
        var editor = new ProjectEditor(document);
        editor.ReplaceSubtitleTextRange(line.Id, 1, 0, "XY");
        var edited = editor.Snapshot.Subtitles[0];
        Assert.Equal("aXYbc", edited.Text);
        var inserted = edited.InactiveKaraoke.Where(clip => clip.Utf16Start >= 1 && clip.Utf16Start <= 3).ToArray();
        Assert.Equal(3, inserted.Length);
        Assert.Equal(source.Start, inserted[0].Start);
        Assert.Equal(source.End, inserted[^1].End);
        Assert.All(inserted, clip =>
        {
            Assert.Equal((source.End - source.Start) / 3, clip.End - clip.Start);
            Assert.Equal(source.HighlightColor, clip.HighlightColor);
            Assert.Equal(source.HighlightKind, clip.HighlightKind);
            Assert.Equal(source.ActiveStyle, clip.ActiveStyle);
            Assert.Equal(source.InactiveStyle, clip.InactiveStyle);
        });
        Assert.Equal(line.Karaoke, edited.Karaoke);
        ProjectValidator.Validate(editor.Snapshot);
        editor.SetSubtitleKaraokeEnabled(line.Id, true);
        var restored = editor.Snapshot.Subtitles[0];
        Assert.Empty(restored.InactiveKaraoke);
        Assert.Equal(5, restored.Karaoke.Length);
        Assert.Equal(edited.Karaoke.AddRange(edited.InactiveKaraoke).OrderBy(clip => clip.Utf16Start), restored.Karaoke);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeletingAllTextClearsBothArraysAndUndoRestoresTheCompleteState(bool mixed)
    {
        var document = Document(mixed);
        var line = document.Subtitles[0];
        var editor = new ProjectEditor(document);
        editor.ReplaceSubtitleTextRange(line.Id, 0, line.Text.Length, string.Empty);
        var edited = editor.Snapshot.Subtitles[0];
        Assert.Empty(edited.Text);
        Assert.Empty(edited.Karaoke);
        Assert.Empty(edited.InactiveKaraoke);
        Assert.Equal(line.KaraokeStyle, edited.KaraokeStyle);
        var empty = editor.Snapshot;
        editor.SetSubtitleKaraokeEnabled(line.Id, true);
        Assert.Same(empty, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void FirstEnableUsesWholeGraphemesAndTheLayerContentOffset()
    {
        var line = new SubtitleLine { Text = "甲e\u0301👩‍💻", Start = new(10), End = new(16) };
        var document = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End, AnimationOffset = new(3, 7) }]
        };
        var editor = new ProjectEditor(document);
        editor.SetSubtitleKaraokeEnabled(line.Id, true);
        var clips = editor.Snapshot.Subtitles[0].Karaoke;
        Assert.Equal(graphemeStarts, clips.Select(clip => clip.Utf16Start));
        Assert.Equal(graphemeLengths, clips.Select(clip => clip.Utf16Length));
        Assert.Equal(new MediaTime(3, 7), clips[0].Start);
        Assert.Equal(new MediaTime(45, 7), clips[^1].End);
        Assert.All(clips, clip => Assert.Equal(new MediaTime(2), clip.End - clip.Start));
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    private static ProjectDocument Document(bool mixed)
    {
        var clips = Enumerable.Range(0, 3).Select(index => new KaraokeSegment(index, 1,
            new(index, 7), new(index + 1, 7), new(2.75, 0.25, 1.125, 0.731))
        {
            HighlightKind = KaraokeHighlightKind.STEP,
            InactiveStyle = new() { StrokeWidth = 1.125, Fill = new(0.125, 0.25, 0.5, 0.731) },
            ActiveStyle = new() { StrokeWidth = 3.875, ShadowOffset = new(-2.25, 4.75) }
        }).ToArray();
        var line = new SubtitleLine
        {
            Text = "abc", End = new(4),
            Karaoke = mixed ? [clips[0]] : [],
            InactiveKaraoke = mixed ? [clips[1], clips[2]] : [.. clips],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "saved highlight", new() { Fill = new(3.25, 0.75, 0.125) })
        };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
