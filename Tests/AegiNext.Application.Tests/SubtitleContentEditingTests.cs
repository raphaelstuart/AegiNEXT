using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class SubtitleContentEditingTests
{
    [Fact]
    public void EqualGraphemeReplacementKeepsClipIdsTimesAndLocalStyles()
    {
        var editor = Editor("a😀c");
        var before = editor.Snapshot;
        var line = before.Subtitles[0];
        editor.ApplySubtitleInlineStyle(line.Id, 1, 2, new() { Bold = true });
        var clips = editor.Snapshot.Subtitles[0].Karaoke;
        editor.ReplaceSubtitleTextRange(line.Id, 1, 2, "字");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal("a字c", result.Text);
        Assert.Equal(clips.Select(value => value.Id), result.Karaoke.Select(value => value.Id));
        Assert.Equal(clips.Select(value => (value.Start, value.End)), result.Karaoke.Select(value => (value.Start, value.End)));
        Assert.Equal(2, result.Karaoke[2].Utf16Start);
        Assert.Equal(new SubtitleInlineSpan(1, 1, new() { Bold = true }), Assert.Single(result.InlineSpans));
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
    }

    [Theory]
    [InlineData(1, "aXYbc")]
    [InlineData(3, "abcXY")]
    public void BoundaryInsertionsStayUntimedAndPreserveEveryExistingClock(int offset, string expected)
    {
        var editor = Editor("abc");
        var line = editor.Snapshot.Subtitles[0];
        var original = line.Karaoke;
        editor.ReplaceSubtitleTextRange(line.Id, offset, 0, "XY");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal(expected, result.Text);
        Assert.Equal(3, result.Karaoke.Length);
        foreach (var clip in original)
        {
            var preserved = Assert.Single(result.Karaoke, value => value.Id == clip.Id);
            Assert.Equal((clip.Start, clip.End), (preserved.Start, preserved.End));
            Assert.Equal(clip.Utf16Start >= offset ? clip.Utf16Start + 2 : clip.Utf16Start, preserved.Utf16Start);
        }
        Assert.DoesNotContain(result.Karaoke, clip => clip.Utf16Start >= offset && clip.Utf16Start < offset + 2);
    }

    [Theory]
    [InlineData(0, "bc", 1, 2)]
    [InlineData(1, "ac", 2, 3)]
    [InlineData(2, "ab", 1, 2)]
    public void DeletedClipLeavesItsTimeEmptyAndKeepsNeighborClocks(int offset, string text, int start, int end)
    {
        var editor = Editor("abc");
        var line = editor.Snapshot.Subtitles[0];
        editor.ReplaceSubtitleTextRange(line.Id, offset, 1, "");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal(text, result.Text);
        var recipient = result.Karaoke[offset == 0 ? 0 : 1];
        Assert.Equal(new MediaTime(start), recipient.Start);
        Assert.Equal(new MediaTime(end), recipient.End);
        Assert.Equal(line.Start, result.Start);
        Assert.Equal(line.End, result.End);
    }

    [Fact]
    public void CombiningAndZwjInsertionsRequireExplicitMergeAcrossTimingBoundaries()
    {
        var editor = Editor("eb");
        var line = editor.Snapshot.Subtitles[0];
        editor.ApplySubtitleInlineStyle(line.Id, 0, 1, new() { Italic = true });
        var beforeCombining = editor.Snapshot;
        Assert.Throws<InvalidOperationException>(() => editor.ReplaceSubtitleTextRange(line.Id, 1, 0, "\u0301"));
        Assert.Same(beforeCombining, editor.Snapshot);
        editor.MergeKaraokeClips(line.Id, line.Karaoke[0].Id, line.Karaoke[1].Id);
        editor.ReplaceSubtitleTextRange(line.Id, 1, 0, "\u0301");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal("e\u0301b", result.Text);
        Assert.Equal(3, result.Karaoke[0].Utf16Length);
        Assert.Equal(line.Karaoke[0].Id, result.Karaoke[0].Id);
        Assert.Equal(2, Assert.Single(result.InlineSpans).Utf16Length);
        ProjectValidator.Validate(editor.Snapshot);

        editor = Editor("👩👧x");
        line = editor.Snapshot.Subtitles[0];
        var beforeJoin = editor.Snapshot;
        Assert.Throws<InvalidOperationException>(() => editor.ReplaceSubtitleTextRange(line.Id, 2, 0, "\u200d"));
        Assert.Same(beforeJoin, editor.Snapshot);
        editor.MergeKaraokeClips(line.Id, line.Karaoke[0].Id, line.Karaoke[1].Id);
        editor.ReplaceSubtitleTextRange(line.Id, 2, 0, "\u200d");
        result = editor.Snapshot.Subtitles[0];
        Assert.Equal("👩‍👧x", result.Text);
        Assert.Equal(2, result.Karaoke.Length);
        Assert.Equal(5, result.Karaoke[0].Utf16Length);
        Assert.Equal(new MediaTime(2), result.Karaoke[0].End);
        Assert.Equal(line.Karaoke[2].Id, result.Karaoke[1].Id);
    }

    [Fact]
    public void InlineStyleMergesSelectedFieldsAndClearRestoresInheritance()
    {
        var editor = Editor("abcd");
        var line = editor.Snapshot.Subtitles[0];
        editor.ApplySubtitleInlineStyle(line.Id, 0, 4, new() { Bold = true });
        editor.ApplySubtitleInlineStyle(line.Id, 1, 2, new() { Italic = true });
        Assert.Equal(3, editor.Snapshot.Subtitles[0].InlineSpans.Length);
        var middle = editor.Snapshot.Subtitles[0].InlineSpans[1];
        Assert.True(middle.Style.Bold);
        Assert.True(middle.Style.Italic);
        editor.ClearSubtitleInlineStyle(line.Id, 1, 2);
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal(2, result.InlineSpans.Length);
        Assert.Equal(line.Karaoke, result.Karaoke);
        editor.ApplySubtitleStyle(line.Id, new() { Italic = true });
        result = editor.Snapshot.Subtitles[0];
        Assert.Empty(result.InlineSpans);
        Assert.True(result.Style.Italic);
        Assert.Equal(line.Karaoke, result.Karaoke);
    }

    [Fact]
    public void InsertedTextInheritsSelectionStyleAndUnaffectedSpansShift()
    {
        var editor = Editor("abcd");
        var id = editor.Snapshot.Subtitles[0].Id;
        editor.ApplySubtitleInlineStyle(id, 1, 1, new() { Bold = true });
        editor.ApplySubtitleInlineStyle(id, 3, 1, new() { Italic = true });
        editor.ReplaceSubtitleTextRange(id, 1, 0, "XY");
        Assert.Equal(new SubtitleInlineSpan(1, 3, new() { Bold = true }), editor.Snapshot.Subtitles[0].InlineSpans[0]);
        Assert.Equal(new SubtitleInlineSpan(5, 1, new() { Italic = true }), editor.Snapshot.Subtitles[0].InlineSpans[1]);
    }

    [Fact]
    public void DurationEditShiftsFollowingClipsExactlyAndHonorsContentOrigin()
    {
        var editor = Editor("abc", new(10), new(16), new(2));
        var before = editor.Snapshot;
        var line = before.Subtitles[0];
        editor.SetKaraokeClipDuration(line.Id, line.Karaoke[1].Id, new(4, 3));
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal(line.Karaoke[0], result.Karaoke[0]);
        Assert.Equal(new MediaTime(13, 3), result.Karaoke[1].End);
        Assert.Equal(new MediaTime(13, 3), result.Karaoke[2].Start);
        Assert.Equal(new MediaTime(16, 3), result.Karaoke[2].End);
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Equal(result, editor.Snapshot.Subtitles[0]);
    }

    [Fact]
    public void NoOpPreservesSnapshotDirtyStateRedoAndEvents()
    {
        var editor = Editor("abc");
        var before = editor.Snapshot;
        var line = before.Subtitles[0];
        editor.ReplaceSubtitleTextRange(line.Id, 0, 1, "z");
        editor.Undo();
        var events = 0;
        editor.Changed += (_, _) => events++;
        editor.ReplaceSubtitleTextRange(line.Id, 0, 1, "a");
        editor.ApplySubtitleInlineStyle(line.Id, 0, 1, new());
        editor.ClearSubtitleInlineStyle(line.Id, 0, 1);
        editor.ApplySubtitleStyle(line.Id, line.Style);
        editor.SetKaraokeClipDuration(line.Id, line.Karaoke[0].Id, new(1));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.CanRedo);
        Assert.False(editor.HasUnsavedChanges);
        Assert.Equal(0, events);
    }

    [Theory]
    [InlineData(1, 1, "x")]
    [InlineData(0, 0, "\0")]
    [InlineData(-1, 0, "x")]
    [InlineData(0, int.MaxValue, "x")]
    public void InvalidTextEditIsAtomic(int start, int length, string text)
    {
        var editor = Editor("😀b");
        var before = editor.Snapshot;
        Assert.ThrowsAny<ArgumentException>(() => editor.ReplaceSubtitleTextRange(before.Subtitles[0].Id, start, length, text));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void UnpairedSurrogateInputIsRejectedWithoutReplacementCharacters()
    {
        var editor = Editor("ab");
        var before = editor.Snapshot;
        Assert.Throws<ArgumentException>(() => editor.ReplaceSubtitleTextRange(
            before.Subtitles[0].Id, 0, 0, new string((char)0xd800, 1)));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void InvalidDurationDoesNotAlterRowBoundsOrHistory()
    {
        var editor = Editor("abc");
        var before = editor.Snapshot;
        var line = before.Subtitles[0];
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetKaraokeClipDuration(line.Id, line.Karaoke[1].Id, new(0)));
        Assert.Throws<ArgumentOutOfRangeException>(() => editor.SetKaraokeClipDuration(line.Id, line.Karaoke[1].Id, new(-1)));
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void ClearingAllTextRemovesEmptyRangesAndKaraoke()
    {
        var editor = Editor("abc");
        var line = editor.Snapshot.Subtitles[0];
        editor.ApplySubtitleInlineStyle(line.Id, 0, 3, new() { Bold = true });
        editor.ReplaceSubtitleTextRange(line.Id, 0, 3, "");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Empty(result.InlineSpans);
        Assert.Empty(result.Karaoke);
        Assert.Null(result.KaraokeStyle);
        Assert.Equal(SubtitleContentKind.PLAIN, result.ContentKind);
    }

    [Fact]
    public void UnequalReplacementAcrossGroupsStaysUntimedAndIsOneTransaction()
    {
        var editor = Editor("abcde");
        var before = editor.Snapshot;
        var line = before.Subtitles[0];
        editor.ReplaceSubtitleTextRange(line.Id, 1, 3, "XY");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal("aXYe", result.Text);
        Assert.Same(line.Karaoke[0], result.Karaoke[0]);
        Assert.Equal(2, result.Karaoke.Length);
        Assert.Equal(line.Karaoke[4].Id, result.Karaoke[1].Id);
        Assert.Equal((line.Karaoke[4].Start, line.Karaoke[4].End), (result.Karaoke[1].Start, result.Karaoke[1].End));
        Assert.Equal(3, result.Karaoke[1].Utf16Start);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.True(editor.Redo());
        Assert.Same(result, editor.Snapshot.Subtitles[0]);
    }

    [Fact]
    public void MultiGraphemeGroupInsertionInheritsTheWholeGroupClock()
    {
        var setup = Editor("abcd");
        var line = setup.Snapshot.Subtitles[0];
        line = line with
        {
            Karaoke = [line.Karaoke[0], new(1, 2, new(1), new(3), SceneColor.White), line.Karaoke[3]]
        };
        var editor = new ProjectEditor(setup.Snapshot with { Subtitles = [line] });
        editor.ReplaceSubtitleTextRange(line.Id, 2, 0, "X");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal("abXcd", result.Text);
        Assert.Equal(3, result.Karaoke.Length);
        Assert.Same(line.Karaoke[0], result.Karaoke[0]);
        Assert.Equal(line.Karaoke[1] with { Utf16Length = 3 }, result.Karaoke[1]);
        Assert.Equal(line.Karaoke[2] with { Utf16Start = 4 }, result.Karaoke[2]);
    }

    [Fact]
    public void UntimedTextInsertionRetainsSparseKaraokeAndUnrelatedWaitingIntervals()
    {
        var setup = Editor("abcde");
        var line = setup.Snapshot.Subtitles[0];
        line = line with { Karaoke = [line.Karaoke[0], line.Karaoke[4] with { Start = new(7), End = new(8) }] };
        var editor = new ProjectEditor(setup.Snapshot with { Subtitles = [line] });
        editor.ReplaceSubtitleTextRange(line.Id, 2, 0, "X");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal(2, result.Karaoke.Length);
        Assert.Same(line.Karaoke[0], result.Karaoke[0]);
        Assert.Equal(line.Karaoke[1].Id, result.Karaoke[1].Id);
        Assert.Equal(new MediaTime(7), result.Karaoke[1].Start);
        Assert.Equal(5, result.Karaoke[1].Utf16Start);
    }

    [Fact]
    public void DeletionKeepsBothDeletedDurationAndExistingWaitEmpty()
    {
        var setup = Editor("abc");
        var line = setup.Snapshot.Subtitles[0];
        line = line with
        {
            Karaoke = [line.Karaoke[0], line.Karaoke[1], line.Karaoke[2] with { Start = new(5), End = new(6) }]
        };
        var editor = new ProjectEditor(setup.Snapshot with { Subtitles = [line] });
        editor.ReplaceSubtitleTextRange(line.Id, 1, 1, "");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Same(line.Karaoke[0], result.Karaoke[0]);
        Assert.Equal(new MediaTime(5), result.Karaoke[1].Start);
        Assert.Equal(new MediaTime(6), result.Karaoke[1].End);
        Assert.Equal(new MediaTime(4), result.Karaoke[1].Start - result.Karaoke[0].End);
    }

    [Fact]
    public void ReapplyingStylesCoalescesSpansAndPreservesRedoForSemanticNoOp()
    {
        var editor = Editor("abcd");
        var id = editor.Snapshot.Subtitles[0].Id;
        editor.ApplySubtitleInlineStyle(id, 0, 2, new() { Bold = true });
        editor.ApplySubtitleInlineStyle(id, 2, 2, new() { Bold = true });
        Assert.Equal(new SubtitleInlineSpan(0, 4, new() { Bold = true }), Assert.Single(editor.Snapshot.Subtitles[0].InlineSpans));
        var before = editor.Snapshot;
        editor.ReplaceSubtitleTextRange(id, 0, 1, "x");
        editor.Undo();
        editor.ApplySubtitleInlineStyle(id, 1, 2, new() { Bold = true });
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.CanRedo);
    }

    [Fact]
    public void InvalidStylesAndMissingClipKeepRedoAndDoNotRaiseChanged()
    {
        var editor = Editor("abc");
        var before = editor.Snapshot;
        var id = before.Subtitles[0].Id;
        editor.ReplaceSubtitleTextRange(id, 0, 1, "x");
        editor.Undo();
        var events = 0;
        editor.Changed += (_, _) => events++;
        Assert.Throws<InvalidDataException>(() => editor.ApplySubtitleInlineStyle(id, 0, 1, new() { FontSize = double.PositiveInfinity }));
        Assert.Throws<KeyNotFoundException>(() => editor.SetKaraokeClipDuration(id, Guid.NewGuid(), new(1)));
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.CanRedo);
        Assert.Equal(0, events);
    }

    [Fact]
    public void CropCompatibilityDoesNotClampExistingClipTimesWhenEditingText()
    {
        var setup = Editor("abc");
        var line = setup.Snapshot.Subtitles[0];
        setup.SetSubtitleTiming(line.Id, new(1), new(2), TimelineEditMode.CROP);
        var editor = new ProjectEditor(setup.Snapshot);
        line = editor.Snapshot.Subtitles[0];
        editor.ReplaceSubtitleTextRange(line.Id, 1, 1, "X");
        Assert.Equal(line.Karaoke, editor.Snapshot.Subtitles[0].Karaoke);
        var before = editor.Snapshot;
        editor.SetKaraokeClipDuration(line.Id, line.Karaoke[1].Id, new(2));
        Assert.Equal(new MediaTime(3), editor.Snapshot.Subtitles[0].Karaoke[1].End);
        Assert.Equal(new MediaTime(4), editor.Snapshot.Subtitles[0].Karaoke[2].End);
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.Equal((line.Start, line.End), (editor.Snapshot.Subtitles[0].Start, editor.Snapshot.Subtitles[0].End));
    }

    [Fact]
    public void ReplacementAcrossWaitingIntervalsClearsOnlyTheTouchedGroups()
    {
        var setup = Editor("ab");
        var line = setup.Snapshot.Subtitles[0];
        line = line with { Karaoke = [line.Karaoke[0], line.Karaoke[1] with { Start = new(2), End = new(3) }] };
        var before = setup.Snapshot with { Subtitles = [line] };
        var editor = new ProjectEditor(before);
        editor.ReplaceSubtitleTextRange(line.Id, 0, 2, "X");
        Assert.Equal("X", editor.Snapshot.Subtitles[0].Text);
        Assert.Empty(editor.Snapshot.Subtitles[0].Karaoke);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        editor.ReplaceSubtitleTextRange(line.Id, 0, 2, "XYZ");
        Assert.Equal("XYZ", editor.Snapshot.Subtitles[0].Text);
        Assert.Empty(editor.Snapshot.Subtitles[0].Karaoke);
    }

    [Fact]
    public void DeletingOneOverlappingGroupPreservesOtherClocksAndEqualEditsKeepTiming()
    {
        var setup = Editor("abc");
        var line = setup.Snapshot.Subtitles[0];
        line = line with
        {
            Karaoke = [line.Karaoke[0] with { End = new(3) }, line.Karaoke[1], line.Karaoke[2] with { Start = new(3), End = new(4) }]
        };
        var before = setup.Snapshot with { Subtitles = [line] };
        var editor = new ProjectEditor(before);
        editor.ReplaceSubtitleTextRange(line.Id, 1, 1, "");
        Assert.Equal(2, editor.Snapshot.Subtitles[0].Karaoke.Length);
        Assert.Equal(line.Karaoke[0], editor.Snapshot.Subtitles[0].Karaoke[0]);
        Assert.Equal(line.Karaoke[2] with { Utf16Start = 1 }, editor.Snapshot.Subtitles[0].Karaoke[1]);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        editor.ReplaceSubtitleTextRange(line.Id, 1, 1, "X");
        Assert.Equal(line.Karaoke, editor.Snapshot.Subtitles[0].Karaoke);
    }

    [Fact]
    public void RichMetadataAndUnrelatedNestedLayersSurviveEditingUndoAndRedo()
    {
        var setup = Editor("abc");
        var line = setup.Snapshot.Subtitles[0];
        var local = new SubtitleInlineStyleOverride
        {
            FontFamily = "serif", FontSize = 92, Bold = true, Underline = true,
            Fill = new(4, -0.1, 2, 0.5), StrokeWidth = 0, ShadowBlur = 3
        };
        var clip = line.Karaoke[1] with
        {
            HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
        };
        line = line with
        {
            InlineSpans = [new(1, 1, local)], Karaoke = line.Karaoke.SetItem(1, clip),
            KaraokeStyleSpans = [new(1, 1, new() { Stroke = new(3, 2, 1, 0.5) }, new() { Fill = SceneColor.Transparent })],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "highlight", new() { Fill = new(4, 2, 1) })
        };
        var other = new SubtitleLine { Text = "other", Start = new(12), End = new(14) };
        var otherLayer = new ProjectLayer
        {
            Id = other.Id, Kind = LayerKind.SUBTITLE, SubtitleId = other.Id, Start = other.Start, End = other.End
        };
        var before = setup.Snapshot with { Subtitles = [line, other], Layers = [setup.Snapshot.Layers[0], otherLayer] };
        var editor = new ProjectEditor(before);
        editor.ReplaceSubtitleTextRange(line.Id, 1, 0, "XY");
        var after = editor.Snapshot;
        var result = after.Subtitles[0];
        Assert.Same(other, after.Subtitles[1]);
        Assert.Equal(before.Layers, after.Layers);
        Assert.Same(line.KaraokeStyle, result.KaraokeStyle);
        Assert.Equal(new SubtitleInlineSpan(1, 3, local), Assert.Single(result.InlineSpans));
        Assert.Equal(clip.HighlightKind, result.Karaoke[1].HighlightKind);
        Assert.Equal(new SubtitleKaraokeStyleSpan(1, 3, line.KaraokeStyleSpans[0].ActiveStyle,
            line.KaraokeStyleSpans[0].InactiveStyle), Assert.Single(result.KaraokeStyleSpans));
        Assert.Equal(result.Karaoke.Length, result.Karaoke.Select(value => value.Id).Distinct().Count());
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(after, editor.Snapshot);
        editor.MarkSaved();
        editor.ApplySubtitleInlineStyle(line.Id, 2, 1, local);
        Assert.Same(after, editor.Snapshot);
        Assert.False(editor.HasUnsavedChanges);
    }

    [Fact]
    public void PlainAndEmptyTextCanBeEditedWithoutCreatingKaraoke()
    {
        var setup = Editor("ab");
        var line = setup.Snapshot.Subtitles[0] with { Karaoke = [] };
        var editor = new ProjectEditor(setup.Snapshot with { Subtitles = [line] });
        editor.ApplySubtitleInlineStyle(line.Id, 1, 1, new() { Bold = true });
        editor.ReplaceSubtitleTextRange(line.Id, 2, 0, "XY");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal(new SubtitleInlineSpan(1, 3, new() { Bold = true }), Assert.Single(result.InlineSpans));
        Assert.Empty(result.Karaoke);
        Assert.Equal(SubtitleContentKind.RICH_TEXT, result.ContentKind);
        editor.ReplaceSubtitleTextRange(line.Id, 0, 4, "");
        editor.ReplaceSubtitleTextRange(line.Id, 0, 0, "😀字");
        result = editor.Snapshot.Subtitles[0];
        Assert.Equal("😀字", result.Text);
        Assert.Empty(result.Karaoke);
        Assert.Empty(result.InlineSpans);
        Assert.Equal(SubtitleContentKind.PLAIN, result.ContentKind);
    }

    [Fact]
    public void ReplacingInsideOneGroupKeepsItsLargeRationalClockWithoutAllocation()
    {
        var setup = Editor("a", end: new(long.MaxValue));
        var line = setup.Snapshot.Subtitles[0];
        var duration = long.MaxValue - 1;
        line = line with { Karaoke = [line.Karaoke[0] with { End = new(duration) }] };
        var editor = new ProjectEditor(setup.Snapshot with { Subtitles = [line] });
        editor.ReplaceSubtitleTextRange(line.Id, 0, 1, "abc");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal(line.Karaoke[0] with { Utf16Length = 3 }, Assert.Single(result.Karaoke));
        Assert.Equal(new MediaTime(duration), result.Karaoke[0].End);
    }

    [Fact]
    public void UnicodeRangeEditsProduceValidSnapshotsAcrossEveryOriginalBoundary()
    {
        string[] samples = ["a😀e\u0301b", "👩👧z", "\r\nx", "a\u200db"];
        string[] replacements = ["", "XY", "\u0301", "\u200d", "😀", "\n"];
        foreach (var text in samples)
        {
            var setup = Editor(text);
            var id = setup.Snapshot.Subtitles[0].Id;
            setup.ApplySubtitleInlineStyle(id, 0, text.Length, new() { Bold = true });
            var before = setup.Snapshot;
            var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
            foreach (var start in boundaries)
            {
                foreach (var end in boundaries.Where(end => end >= start))
                {
                    foreach (var replacement in replacements)
                    {
                        var editor = new ProjectEditor(before);
                        try
                        {
                            editor.ReplaceSubtitleTextRange(id, start, end - start, replacement);
                        }
                        catch (InvalidOperationException)
                        {
                            Assert.Same(before, editor.Snapshot);
                            Assert.False(editor.CanUndo);
                            continue;
                        }
                        Assert.Equal(text[..start] + replacement + text[end..], editor.Snapshot.Subtitles[0].Text);
                        ProjectValidator.Validate(editor.Snapshot);
                        Assert.Equal(before.Layers, editor.Snapshot.Layers);
                    }
                }
            }
        }
    }

    private static ProjectEditor Editor(string text, MediaTime? start = null, MediaTime? end = null, MediaTime? offset = null)
    {
        var boundaries = System.Globalization.StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        var line = new SubtitleLine
        {
            Text = text, Start = start ?? new(0), End = end ?? new(10),
            Karaoke = boundaries.SkipLast(1).Select((value, index) => new KaraokeSegment(value,
                boundaries[index + 1] - value, (offset ?? new(0)) + new MediaTime(index),
                (offset ?? new(0)) + new MediaTime(index + 1), SceneColor.White)).ToImmutableArray()
        };
        return new(new()
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End, AnimationOffset = offset ?? new(0) }]
        });
    }
}
