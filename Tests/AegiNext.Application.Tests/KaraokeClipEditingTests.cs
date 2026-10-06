using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class KaraokeClipEditingTests
{
    [Fact]
    public void GraphemeSplitKeepsUnicodeAppearanceExactCropTimesAndSingleUndo()
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(0), new(1), "a😀e\u0301👩‍👧");
        setup.ApplySubtitleInlineStyle(id, 1, 2, new() { Bold = true });
        var original = new KaraokeSegment(0, setup.Snapshot.Subtitles[0].Text.Length, new(1, 3), new(7, 3), new(1, 0, 0))
        {
            HighlightKind = KaraokeHighlightKind.OUTLINE_STEP, ActiveStyle = new() { StrokeWidth = 4 }
        };
        var before = setup.Snapshot;
        var editor = new ProjectEditor(before);
        editor.Apply("Normalize legacy clip", document => ProjectEditingOperations.SplitKaraokeClipIntoGraphemes(document with
        {
            Subtitles = [document.Subtitles[0] with { Karaoke = [original] }]
        }, id, original.Id));
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal([1, 2, 2, 5], result.Karaoke.Select(clip => clip.Utf16Length));
        Assert.Equal(original.Id, result.Karaoke[0].Id);
        Assert.Equal(4, result.Karaoke.Select(clip => clip.Id).Distinct().Count());
        Assert.All(result.Karaoke, clip =>
        {
            Assert.Equal(new MediaTime(1, 2), clip.End - clip.Start);
            Assert.Equal(original.HighlightKind, clip.HighlightKind);
            Assert.Equal(original.ActiveStyle, clip.ActiveStyle);
        });
        Assert.Equal(original.Start, result.Karaoke[0].Start);
        Assert.Equal(original.End, result.Karaoke[^1].End);
        Assert.Equal(before.Subtitles[0].InlineSpans, result.InlineSpans);
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Equal(result.Karaoke, editor.Snapshot.Subtitles[0].Karaoke);
    }

    [Fact]
    public void CompatibilitySplitAndMergeReturnPerGraphemeClipsWithExactEnvelope()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(5), "a😀b");
        var before = editor.Snapshot with
        {
            Subtitles = [editor.Snapshot.Subtitles[0] with { Karaoke = [new(0, 4, new(1), new(4), SceneColor.White)] }]
        };
        var clip = before.Subtitles[0].Karaoke[0];
        var split = ProjectEditingOperations.SplitKaraokeClip(before, id, clip.Id, 3);
        var divided = split.Subtitles[0].Karaoke;
        Assert.Equal([1, 2, 1], divided.Select(value => value.Utf16Length));
        Assert.Equal(new MediaTime(3), divided[1].End);
        Assert.Equal(new MediaTime(3), divided[2].Start);
        var merged = ProjectEditingOperations.MergeKaraokeClips(split, id, divided[0].Id, divided[1].Id).Subtitles[0].Karaoke;
        Assert.Equal([1, 2, 1], merged.Select(value => value.Utf16Length));
        Assert.Equal(clip.Id, merged[0].Id);
        Assert.Equal(clip.Start, merged[0].Start);
        Assert.Equal(clip.End, merged[^1].End);
        var explicitSplit = ProjectEditingOperations.SplitKaraokeClip(before, id, clip.Id, 1, new(3, 2));
        Assert.Equal(new MediaTime(3, 2), explicitSplit.Subtitles[0].Karaoke[0].End);
        Assert.Equal(new MediaTime(3, 2), explicitSplit.Subtitles[0].Karaoke[1].Start);
        Assert.Equal(new MediaTime(11, 4), explicitSplit.Subtitles[0].Karaoke[1].End);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void SplitRejectsEdgesAndSurrogateInteriorWithoutChangingHistory(int offset)
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(0), new(3), "a😀b");
        var before = setup.Snapshot;
        var legacy = before with
        {
            Subtitles = [before.Subtitles[0] with { Karaoke = [new(0, 4, new(0), new(3), SceneColor.White)] }]
        };
        var editor = new ProjectEditor(before);
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.Apply("Invalid legacy split", _ =>
            ProjectEditingOperations.SplitKaraokeClip(legacy, id, legacy.Subtitles[0].Karaoke[0].Id, offset)));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MergeRejectsWaitsAndDifferentHighlightConfigurations(bool waiting)
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(0), new(4), "ab");
        setup.UpdateSubtitle(id, line => line with
        {
            Karaoke =
            [
                new(0, 1, new(0), new(1), SceneColor.White),
                new(1, 1, waiting ? new(2) : new(1), new(3), waiting ? SceneColor.White : SceneColor.Black)
            ]
        });
        var before = setup.Snapshot;
        var editor = new ProjectEditor(before);
        var clips = before.Subtitles[0].Karaoke;
        Assert.Throws<InvalidOperationException>(() => editor.MergeKaraokeClips(id, clips[0].Id, clips[1].Id));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void SplittingAnExistingSingleGraphemeDoesNotCreateUndoHistory()
    {
        var setup = new ProjectEditor();
        var id = setup.AddSubtitle(new(0), new(3), "👩‍👧");
        setup.UpdateSubtitle(id, line => line with { Karaoke = [new(0, line.Text.Length, new(0), new(3), SceneColor.White)] });
        var editor = new ProjectEditor(setup.Snapshot);
        editor.SplitKaraokeClipIntoGraphemes(id, editor.Snapshot.Subtitles[0].Karaoke[0].Id);
        Assert.False(editor.CanUndo);
    }
}
