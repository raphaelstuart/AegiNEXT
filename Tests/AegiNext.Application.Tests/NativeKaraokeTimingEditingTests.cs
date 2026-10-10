using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class NativeKaraokeTimingEditingTests
{
    [Fact]
    public void LinkedRangePreservesCueLayerAndCachedTimingAndHasOneUndoRedo()
    {
        var document = Document("abcd", [new(0, 1, new(1), new(2), SceneColor.White),
            new(1, 1, new(3), new(4), SceneColor.White), new(2, 1, new(5), new(6), SceneColor.White)]);
        var line = document.Subtitles[0] with
        {
            InactiveKaraoke = [new(3, 1, new(7), new(8), SceneColor.White)]
        };
        document = document with { Subtitles = [line] };
        var editor = new ProjectEditor(document);
        editor.SetKaraokeClipRange(line.Id, line.Karaoke[1].Id, new(7, 2), new(9, 2), true);
        var changed = editor.Snapshot;
        var changedLine = changed.Subtitles[0];
        Assert.Equal(new MediaTime(3, 2), changedLine.Karaoke[0].Start);
        Assert.Equal(new MediaTime(11, 2), changedLine.Karaoke[2].Start);
        Assert.Equal(line.InactiveKaraoke, changedLine.InactiveKaraoke);
        Assert.Equal(line.Start, changedLine.Start);
        Assert.Equal(line.End, changedLine.End);
        Assert.Equal(document.Layers, changed.Layers);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(changed, editor.Snapshot);
        editor.SetKaraokeClipRange(line.Id, line.Karaoke[1].Id, new(7, 2), new(9, 2), true);
        Assert.Same(changed, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void OpeningAndEditingPreserveTheWholeSyllable()
    {
        var document = Document("你好👩‍💻", [new(0, 7, new(1, 3), new(7, 3), SceneColor.White)]);
        var editor = new ProjectEditor(document);
        Assert.Same(document, editor.Snapshot);
        editor.ApplySubtitleInlineStyle(document.Subtitles[0].Id, 1, 1, new() { Bold = true });
        Assert.Equal(document.Subtitles[0].Karaoke, editor.Snapshot.Subtitles[0].Karaoke);
        Assert.Equal(document.Layers, editor.Snapshot.Layers);
    }

    [Fact]
    public void IndependentRangeEditAllowsOverlapAndBackwardsTimingWithSingleUndo()
    {
        var document = Document("abc", [new(0, 1, new(1), new(2), SceneColor.White),
            new(1, 1, new(3), new(4), SceneColor.White), new(2, 1, new(5), new(6), SceneColor.White)]);
        var editor = new ProjectEditor(document);
        var line = document.Subtitles[0];
        editor.SetKaraokeClipRange(line.Id, line.Karaoke[1].Id, new(0), new(3, 2));
        var changed = editor.Snapshot;
        Assert.Same(line.Karaoke[0], changed.Subtitles[0].Karaoke[0]);
        Assert.Same(line.Karaoke[2], changed.Subtitles[0].Karaoke[2]);
        Assert.Equal(new MediaTime(0), changed.Subtitles[0].Karaoke[1].Start);
        Assert.Equal(new MediaTime(3, 2), changed.Subtitles[0].Karaoke[1].End);
        Assert.Equal(document.Layers, changed.Layers);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(changed, editor.Snapshot);
    }

    [Fact]
    public void DeletingAWholeCharacterLeavesItsTimeEmpty()
    {
        var document = Document("abc", [new(0, 1, new(0), new(1), SceneColor.White),
            new(1, 1, new(1), new(2), SceneColor.White), new(2, 1, new(2), new(3), SceneColor.White)]);
        var line = document.Subtitles[0];
        var edited = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 1, 1, "").Subtitles[0];
        Assert.Equal("ac", edited.Text);
        Assert.Equal(2, edited.Karaoke.Length);
        Assert.Same(line.Karaoke[0], edited.Karaoke[0]);
        Assert.Equal(line.Karaoke[2] with { Utf16Start = 1 }, edited.Karaoke[1]);
        Assert.Equal(new MediaTime(1), edited.Karaoke[0].End);
        Assert.Equal(new MediaTime(2), edited.Karaoke[1].Start);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void InsertionInheritsOnlyInsideAnIntactGroup(int position, bool inherits)
    {
        var document = Document("ab", [new(0, 2, new(1), new(3), SceneColor.White)]);
        var line = document.Subtitles[0];
        var edited = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, position, 0, "字").Subtitles[0];
        var segment = Assert.Single(edited.Karaoke);
        Assert.Equal(inherits ? 3 : 2, segment.Utf16Length);
        Assert.Equal(position == 0 ? 1 : 0, segment.Utf16Start);
        Assert.Equal(line.Karaoke[0].Id, segment.Id);
        Assert.Equal(line.Karaoke[0].Start, segment.Start);
        Assert.Equal(line.Karaoke[0].End, segment.End);
    }

    [Fact]
    public void CrossGroupReplacementDoesNotInventTimingOrRejectBackwardsTimes()
    {
        var document = Document("abcd", [new(0, 2, new(3), new(4), SceneColor.White),
            new(2, 2, new(1), new(2), SceneColor.White)]);
        var line = document.Subtitles[0];
        var edited = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 1, 2, "XYZ").Subtitles[0];
        Assert.Equal("aXYZd", edited.Text);
        Assert.Equal(2, edited.Karaoke.Length);
        Assert.Equal(line.Karaoke[0] with { Utf16Length = 1 }, edited.Karaoke[0]);
        Assert.Equal(line.Karaoke[1] with { Utf16Start = 4, Utf16Length = 1 }, edited.Karaoke[1]);
    }

    [Fact]
    public void SplittingOnlyTheSelectedGroupPreservesTheOtherGroup()
    {
        var document = Document("a😀你好", [new(0, 3, new(1, 3), new(7, 3), SceneColor.White),
            new(3, 2, new(4), new(6), SceneColor.White)]);
        var line = document.Subtitles[0];
        var split = ProjectEditingOperations.SplitKaraokeClipIntoGraphemes(document, line.Id, line.Karaoke[0].Id);
        var clips = split.Subtitles[0].Karaoke;
        Assert.Equal([1, 2, 2], clips.Select(clip => clip.Utf16Length));
        Assert.Equal(new MediaTime(4, 3), clips[0].End);
        Assert.Equal(clips[0].End, clips[1].Start);
        Assert.Same(line.Karaoke[1], clips[2]);
    }

    [Fact]
    public void ManualTimingCreatesOnlyTheSelectedUnassignedRange()
    {
        var document = Document("a😀b", []);
        var line = document.Subtitles[0];
        var created = ProjectEditingOperations.CreateKaraokeClip(document, line.Id, 1, 2, new(2), new(3));
        var clip = Assert.Single(created.Subtitles[0].Karaoke);
        Assert.Equal(1, clip.Utf16Start);
        Assert.Equal(2, clip.Utf16Length);
        Assert.Equal(new MediaTime(2), clip.Start);
        Assert.Equal(new MediaTime(3), clip.End);
        Assert.Equal(document.Layers, created.Layers);
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.CreateKaraokeClip(created,
            line.Id, 0, 4, new(0), new(1)));
    }

    [Fact]
    public void LeadingDelayUsesTheEarliestTimeRatherThanTextOrder()
    {
        var document = Document("ab", [new(0, 1, new(3), new(4), SceneColor.White),
            new(1, 1, new(1), new(2), SceneColor.White)]);
        var line = document.Subtitles[0];
        var shifted = ProjectEditingOperations.SetKaraokeLeadingDelay(document, line.Id, new(2), MediaTime.Zero).Subtitles[0];
        Assert.Equal(new MediaTime(4), shifted.Karaoke[0].Start);
        Assert.Equal(new MediaTime(2), shifted.Karaoke[1].Start);
        Assert.Equal(line.Karaoke.Select(clip => clip.End - clip.Start), shifted.Karaoke.Select(clip => clip.End - clip.Start));
    }

    [Fact]
    public void InvalidRangeDoesNotChangeHistory()
    {
        var document = Document("ab", [new(0, 2, new(1), new(2), SceneColor.White)]);
        var editor = new ProjectEditor(document);
        var line = document.Subtitles[0];
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetKaraokeClipRange(line.Id,
            line.Karaoke[0].Id, new(-1), new(2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetKaraokeClipRange(line.Id,
            line.Karaoke[0].Id, new(2), new(2)));
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void PartialGroupDeletionPreservesItsEntireClock()
    {
        var document = Document("a😀b", [new(0, 4, new(1, 7), new(11, 7), SceneColor.White)]);
        var line = document.Subtitles[0];
        var edited = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 1, 2, "").Subtitles[0];
        Assert.Equal("ab", edited.Text);
        Assert.Equal(line.Karaoke[0] with { Utf16Length = 2 }, Assert.Single(edited.Karaoke));
    }

    [Fact]
    public void EqualCountCrossGroupReplacementKeepsEachClock()
    {
        var document = Document("abcd", [new(0, 2, new(3), new(4), SceneColor.White),
            new(2, 2, new(1), new(2), SceneColor.White)]);
        var line = document.Subtitles[0];
        var edited = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 1, 2, "😀字").Subtitles[0];
        Assert.Equal("a😀字d", edited.Text);
        Assert.Equal(line.Karaoke[0] with { Utf16Length = 3 }, edited.Karaoke[0]);
        Assert.Equal(line.Karaoke[1] with { Utf16Start = 3 }, edited.Karaoke[1]);
    }

    [Fact]
    public void UnicodeJoinAcrossTimingOwnershipIsRejectedAtomically()
    {
        var document = Document("ab", [new(0, 1, new(0), new(1), SceneColor.White)]);
        var editor = new ProjectEditor(document);
        Assert.Throws<InvalidOperationException>(() => editor.ReplaceSubtitleTextRange(document.Subtitles[0].Id,
            1, 0, "\u0301"));
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    private static ProjectDocument Document(string text, System.Collections.Immutable.ImmutableArray<KaraokeSegment> clips)
    {
        var line = new SubtitleLine { Text = text, End = new(10), Karaoke = clips };
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
