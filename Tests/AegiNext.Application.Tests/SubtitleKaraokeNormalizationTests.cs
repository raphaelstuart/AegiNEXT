using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class SubtitleKaraokeNormalizationTests
{
    [Fact]
    public void NormalizationIsIdempotentAndRetainsUnchangedRecordAndArrayIdentity()
    {
        var document = LegacyDocument("abc");
        var normalized = LegacySubtitleKaraokeMigration.Upgrade(document);
        var line = normalized.Subtitles[0];
        Assert.Same(line, LegacySubtitleKaraokeMigration.Upgrade(line));
        Assert.Same(normalized, LegacySubtitleKaraokeMigration.Upgrade(normalized));
        Assert.Same(document.Subtitles[0].InlineSpans[0], line.InlineSpans[0]);
        Assert.Equal(document.Layers, normalized.Layers);
        Assert.Single(document.Subtitles[0].Karaoke);
        var editor = new ProjectEditor(document);
        var snapshot = editor.Snapshot;
        Assert.Same(document.Subtitles[0], snapshot.Subtitles[0]);
        Assert.Single(snapshot.Subtitles[0].Karaoke);
        Assert.False(editor.HasUnsavedChanges);
        Assert.False(editor.CanUndo);
        editor.ReplaceSubtitleTextRange(line.Id, 0, 0, "");
        Assert.Same(snapshot, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [Fact]
    public void InactiveWordNormalizationRetainsEnablementClockStyleAndUnchangedIdentities()
    {
        var document = LegacyDocument("a😀e\u0301👩‍💻z");
        var source = document.Subtitles[0];
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Saved", new() { Fill = new(4, 0, 0) });
        source = source with { Karaoke = [], InactiveKaraoke = source.Karaoke, KaraokeStyle = highlight };
        document = document with { Subtitles = [source] };
        var normalized = LegacySubtitleKaraokeMigration.Upgrade(document);
        var line = normalized.Subtitles[0];
        Assert.Empty(line.Karaoke);
        Assert.True(source.Karaoke == line.Karaoke);
        Assert.Equal(SubtitleContentKind.RICH_TEXT, line.ContentKind);
        Assert.Equal([1, 2, 2, 5, 1], line.InactiveKaraoke.Select(clip => clip.Utf16Length));
        Assert.Equal(source.InactiveKaraoke[0].Id, line.InactiveKaraoke[0].Id);
        Assert.Equal(source.InactiveKaraoke[0].Start, line.InactiveKaraoke[0].Start);
        Assert.Equal(source.InactiveKaraoke[0].End, line.InactiveKaraoke[^1].End);
        Assert.All(line.InactiveKaraoke, clip =>
        {
            Assert.Equal(new MediaTime(2, 5), clip.End - clip.Start);
            Assert.Equal(source.InactiveKaraoke[0].HighlightKind, clip.HighlightKind);
        });
        Assert.Same(highlight, line.KaraokeStyle);
        Assert.True(source.InlineSpans == line.InlineSpans);
        Assert.True(document.Layers == normalized.Layers);
        Assert.Single(source.InactiveKaraoke);
        Assert.Same(line, LegacySubtitleKaraokeMigration.Upgrade(line));
        Assert.Same(normalized, LegacySubtitleKaraokeMigration.Upgrade(normalized));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizingOneArrayPreservesTheOtherCanonicalArray(bool normalizeActive)
    {
        var source = LegacyDocument("ab😀e\u0301").Subtitles[0];
        var word = source.Karaoke[0] with { Utf16Start = 2, Utf16Length = 4, Start = new(4), End = new(7) };
        var canonical = source.Karaoke[0] with { Id = Guid.NewGuid(), Utf16Length = 1, Start = new(0), End = new(1) };
        source = source with
        {
            Karaoke = normalizeActive ? [word] : [canonical],
            InactiveKaraoke = normalizeActive ? [canonical] : [word]
        };
        var normalized = LegacySubtitleKaraokeMigration.Upgrade(source);
        var unchanged = normalizeActive ? normalized.InactiveKaraoke : normalized.Karaoke;
        var originalUnchanged = normalizeActive ? source.InactiveKaraoke : source.Karaoke;
        var changed = normalizeActive ? normalized.Karaoke : normalized.InactiveKaraoke;
        Assert.True(originalUnchanged == unchanged);
        Assert.Same(canonical, Assert.Single(unchanged));
        Assert.Equal([2, 4], changed.Select(clip => clip.Utf16Start));
        Assert.Equal([2, 2], changed.Select(clip => clip.Utf16Length));
        Assert.Equal(word.Id, changed[0].Id);
        Assert.Equal(new MediaTime(4), changed[0].Start);
        Assert.Equal(new MediaTime(7), changed[^1].End);
        Assert.NotEqual(canonical.Id, changed[1].Id);
        Assert.Same(normalized, LegacySubtitleKaraokeMigration.Upgrade(normalized));
    }

    [Fact]
    public void BothArraysNormalizeWithSharedUniqueIdsWithoutEnablingInactiveRanges()
    {
        var source = LegacyDocument("abcdef").Subtitles[0];
        var first = source.Karaoke[0] with { Utf16Length = 2, Start = new(0), End = new(2) };
        var second = first with { Id = Guid.NewGuid(), Utf16Start = 2, Start = new(3), End = new(5) };
        var third = first with { Id = Guid.NewGuid(), Utf16Start = 4, Start = new(6), End = new(8) };
        source = source with { Karaoke = [first, third], InactiveKaraoke = [second] };
        var normalized = LegacySubtitleKaraokeMigration.Upgrade(source);
        Assert.Equal([0, 1, 4, 5], normalized.Karaoke.Select(clip => clip.Utf16Start));
        Assert.Equal([2, 3], normalized.InactiveKaraoke.Select(clip => clip.Utf16Start));
        Assert.Equal(first.Id, normalized.Karaoke[0].Id);
        Assert.Equal(third.Id, normalized.Karaoke[2].Id);
        Assert.Equal(second.Id, normalized.InactiveKaraoke[0].Id);
        Assert.Equal(6, normalized.Karaoke.Concat(normalized.InactiveKaraoke).Select(clip => clip.Id).Distinct().Count());
        ProjectValidator.ValidateSubtitleKaraoke(normalized);
        Assert.Same(normalized, LegacySubtitleKaraokeMigration.Upgrade(normalized));
        Assert.Equal(2, source.Karaoke.Length);
        Assert.Single(source.InactiveKaraoke);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void NormalizationRejectsInvalidInactiveDataAndCrossArrayConflicts(int invalidValue)
    {
        var source = LegacyDocument("a😀b").Subtitles[0];
        var active = source.Karaoke[0] with { Utf16Length = 1 };
        var inactive = active with { Id = Guid.NewGuid(), Utf16Start = 1, Utf16Length = 2 };
        source = source with { Karaoke = [active], InactiveKaraoke = [inactive] };
        var invalid = invalidValue switch
        {
            0 => source with { InactiveKaraoke = default },
            1 => source with { InactiveKaraoke = [null!] },
            2 => source with { InactiveKaraoke = [inactive with { Utf16Length = 1 }] },
            3 => source with { InactiveKaraoke = [inactive with { Id = active.Id }] },
            4 => source with { InactiveKaraoke = [inactive with { Utf16Start = 0, Utf16Length = 1 }] },
            5 => source with { InactiveKaraoke = [inactive with { HighlightColor = new(0, 0, 0, 2) }] },
            _ => source with { KaraokeStyleSpans = [new(1, 2, null, new() { ShadowBlur = -1 })] }
        };
        Assert.Throws<InvalidDataException>(() => LegacySubtitleKaraokeMigration.Upgrade(invalid));
        Assert.Same(active, Assert.Single(source.Karaoke));
        Assert.Same(inactive, Assert.Single(source.InactiveKaraoke));
    }

    [Fact]
    public void SparseTextWaitsLegacyOverlapAndUnchangedSingleClipRemainExact()
    {
        var document = LegacyDocument("ab😀e\u0301x");
        var source = document.Subtitles[0];
        var first = source.Karaoke[0] with { Utf16Length = 2, Start = new(0), End = new(1) };
        var second = first with { Id = Guid.NewGuid(), Utf16Start = 2, Utf16Length = 4, Start = new(4), End = new(7) };
        var last = first with { Id = Guid.NewGuid(), Utf16Start = 6, Utf16Length = 1, Start = new(6), End = new(8) };
        var normalized = LegacySubtitleKaraokeMigration.Upgrade(source with { Karaoke = [first, second, last] });
        Assert.Equal([1, 1, 2, 2, 1], normalized.Karaoke.Select(value => value.Utf16Length));
        Assert.Equal(new MediaTime(1), normalized.Karaoke[1].End);
        Assert.Equal(new MediaTime(4), normalized.Karaoke[2].Start);
        Assert.Equal(new MediaTime(7), normalized.Karaoke[3].End);
        Assert.Same(last, normalized.Karaoke[^1]);
        Assert.Equal(first.Id, normalized.Karaoke[0].Id);
        Assert.Equal(second.Id, normalized.Karaoke[2].Id);

        var sparse = LegacySubtitleKaraokeMigration.Upgrade(source with { Karaoke = [second] });
        Assert.Equal([2, 4], sparse.Karaoke.Select(value => value.Utf16Start));
        Assert.Equal(new MediaTime(4), sparse.Karaoke[0].Start);
        Assert.Equal(new MediaTime(7), sparse.Karaoke[^1].End);
    }

    [Fact]
    public void EqualCountUnicodeReplacementKeepsEveryCanonicalIdAndExactTime()
    {
        var editor = new ProjectEditor(LegacyDocument("a😀e\u0301👩‍💻z"));
        var before = editor.Snapshot;
        var line = before.Subtitles[0];
        editor.ReplaceSubtitleTextRange(line.Id, 1, 9, "字👍🏽🇨🇳");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Equal("a字👍🏽🇨🇳z", result.Text);
        Assert.Equal(line.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)),
            result.Karaoke.Select(clip => (clip.Id, clip.Start, clip.End)));
        Assert.Equal([11], result.Karaoke.Select(clip => clip.Utf16Length));
        Assert.Equal(before.Layers, editor.Snapshot.Layers);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizationUsesExactReducedRationalsAndRejectsUnrepresentableOutputAtomically(bool inactive)
    {
        var source = LegacyDocument("ab").Subtitles[0];
        var clip = source.Karaoke[0] with { Start = new(1, long.MaxValue), End = new(3, long.MaxValue) };
        var rational = source with
        {
            Karaoke = inactive ? [] : [clip],
            InactiveKaraoke = inactive ? [clip] : []
        };
        var normalized = LegacySubtitleKaraokeMigration.Upgrade(rational);
        var segments = inactive ? normalized.InactiveKaraoke : normalized.Karaoke;
        Assert.Equal(new MediaTime(2, long.MaxValue), segments[0].End);
        Assert.Equal(segments[0].End, segments[1].Start);
        clip = clip with { End = new(2, long.MaxValue) };
        var impossible = source with
        {
            Karaoke = inactive ? [] : [clip],
            InactiveKaraoke = inactive ? [clip] : []
        };
        Assert.Throws<OverflowException>(() => LegacySubtitleKaraokeMigration.Upgrade(impossible));
        var unchanged = Assert.Single(inactive ? impossible.InactiveKaraoke : impossible.Karaoke);
        Assert.Same(clip, unchanged);
        Assert.Equal(new MediaTime(2, long.MaxValue), unchanged.End);
    }

    [Fact]
    public void SharedNormalizationRejectsInvalidGraphemeRangesAndDuplicateIdentity()
    {
        var source = LegacyDocument("a😀b").Subtitles[0];
        Assert.Throws<InvalidDataException>(() => LegacySubtitleKaraokeMigration.Upgrade(source with
        {
            Karaoke = [source.Karaoke[0] with { Utf16Start = 1, Utf16Length = 1 }]
        }));
        Assert.Throws<InvalidDataException>(() => LegacySubtitleKaraokeMigration.Upgrade(source with
        {
            Karaoke = [source.Karaoke[0] with { Utf16Length = 1 }, source.Karaoke[0] with { Utf16Start = 3, Utf16Length = 1 }]
        }));
        Assert.Throws<InvalidDataException>(() => LegacySubtitleKaraokeMigration.Upgrade(source with { Text = "\ud800" }));
    }

    [Fact]
    public void BothSubtitleImportEntryPointsPreserveGroupsAndVisualConfiguration()
    {
        var source = LegacyDocument("a😀").Subtitles[0];
        var document = new ProjectDocument();
        var independent = ProjectEditingOperations.ImportSubtitleLines(document, [source], "ASS");
        var assigned = ProjectEditingOperations.CreateSubtitleClips(document, [source], document.Tracks[0].Id);
        foreach (var imported in new[] { independent, assigned })
        {
            var line = Assert.Single(imported.Subtitles);
            Assert.Equal([3], line.Karaoke.Select(clip => clip.Utf16Length));
            Assert.Equal(source.KaraokeStyleSpans, line.KaraokeStyleSpans);
            Assert.Equal(source.Karaoke[0].Id, line.Karaoke[0].Id);
            foreach (var clip in line.Karaoke)
            {
                Assert.Equal(source.Karaoke[0].HighlightKind, clip.HighlightKind);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EditingAndClearingTextKeepsReusableHighlightConfigurationEvenWithoutTiming(bool timed)
    {
        var document = LegacyDocument("abc");
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "reusable", new() { Fill = new(0, 1, 0) });
        var line = document.Subtitles[0] with
        {
            Karaoke = timed ? document.Subtitles[0].Karaoke : [],
            KaraokeStyle = highlight
        };
        var editor = new ProjectEditor(document with { Subtitles = [line] });
        editor.ReplaceSubtitleTextRange(line.Id, 1, 1, "X");
        Assert.Same(highlight, editor.Snapshot.Subtitles[0].KaraokeStyle);
        editor.ReplaceSubtitleTextRange(line.Id, 0, 3, "");
        var cleared = editor.Snapshot;
        Assert.Empty(cleared.Subtitles[0].Karaoke);
        Assert.Equal(SubtitleContentKind.PLAIN, cleared.Subtitles[0].ContentKind);
        Assert.Same(highlight, cleared.Subtitles[0].KaraokeStyle);
        editor.ReplaceSubtitleTextRange(line.Id, 0, 0, "你好👩‍💻");
        var result = editor.Snapshot.Subtitles[0];
        Assert.Empty(result.Karaoke);
        Assert.Same(highlight, result.KaraokeStyle);
        Assert.Equal(highlight, ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot)).Subtitles[0].KaraokeStyle);
        Assert.True(editor.Undo());
        Assert.Same(cleared, editor.Snapshot);
    }

    [Fact]
    public async Task LoadingLegacyWordClipDividesWholeGraphemesWithoutRewritingTheFile()
    {
        var document = LegacyDocument("a😀e\u0301👩‍💻z");
        var line = document.Subtitles[0];
        var clip = line.Karaoke[0];
        var node = System.Text.Json.Nodes.JsonNode.Parse(ProjectStore.Serialize(document))!.AsObject();
        node["version"] = 10;
        LegacySubtitleMarginsJsonFixture.DowngradeProject(node);
        var bytes = System.Text.Encoding.UTF8.GetBytes(node.ToJsonString());
        var path = Path.Combine(Path.GetTempPath(), $"aeginext-karaoke-{Guid.NewGuid():N}.aeginext");
        try
        {
            await File.WriteAllBytesAsync(path, bytes);
            var loaded = await ProjectStore.LoadAsync(path);
            var result = loaded.Subtitles[0];
            Assert.Equal([1, 2, 2, 5, 1], result.Karaoke.Select(value => value.Utf16Length));
            Assert.Equal(clip.Id, result.Karaoke[0].Id);
            Assert.Equal(5, result.Karaoke.Select(value => value.Id).Distinct().Count());
            Assert.All(result.Karaoke, value =>
            {
                Assert.Equal(new MediaTime(2, 5), value.End - value.Start);
                Assert.Equal(clip.HighlightKind, value.HighlightKind);
            });
            Assert.Equal(clip.Start, result.Karaoke[0].Start);
            Assert.Equal(clip.End, result.Karaoke[^1].End);
            Assert.Equal(line.InlineSpans.ToArray(), result.InlineSpans.ToArray());
            Assert.Equal(document.Layers.Select(layer => (layer.Id, layer.Start, layer.End, layer.AnimationOffset)),
                loaded.Layers.Select(layer => (layer.Id, layer.Start, layer.End, layer.AnimationOffset)));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
            var roundtrip = ProjectStore.Deserialize(ProjectStore.Serialize(loaded));
            Assert.Equal(result.Karaoke.ToArray(), roundtrip.Subtitles[0].Karaoke.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UpdatingWordClipsPreservesGroupsInOneUndoTransaction()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(1), "a😀e\u0301");
        var before = editor.Snapshot;
        var clip = new KaraokeSegment(0, 5, new(1), new(4), SceneColor.White);
        editor.UpdateSubtitle(id, line => line with { Karaoke = [clip] });
        var normalized = editor.Snapshot;
        Assert.Equal([5], normalized.Subtitles[0].Karaoke.Select(value => value.Utf16Length));
        Assert.Equal(clip.Id, normalized.Subtitles[0].Karaoke[0].Id);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.True(editor.Redo());
        Assert.Same(normalized, editor.Snapshot);
    }

    [Fact]
    public void InsertingIntoNativeWordExtendsItsTextWithoutRedistributingTime()
    {
        var document = LegacyDocument("abcd");
        var line = document.Subtitles[0];
        var result = ProjectEditingOperations.ReplaceSubtitleTextRange(document, line.Id, 1, 0, "XY").Subtitles[0];
        Assert.Equal("aXYbcd", result.Text);
        var group = Assert.Single(result.Karaoke);
        Assert.Equal(line.Karaoke[0] with { Utf16Length = 6 }, group);

    }

    [Fact]
    public void DurationCanOverflowCueAndShrinkBackWhileKeepingWaitingIntervalsAndHistory()
    {
        var document = LegacyDocument("ab");
        var line = document.Subtitles[0];
        line = line with
        {
            Karaoke =
            [
                line.Karaoke[0] with { Utf16Length = 1, Start = new(1), End = new(2) },
                line.Karaoke[0] with { Id = Guid.NewGuid(), Utf16Start = 1, Utf16Length = 1, Start = new(3), End = new(4) }
            ]
        };
        document = document with { Subtitles = [line] };
        var editor = new ProjectEditor(document);
        editor.SetKaraokeClipDuration(line.Id, line.Karaoke[0].Id, new(10));
        var expanded = editor.Snapshot;
        var result = expanded.Subtitles[0];
        Assert.Equal(new MediaTime(11), result.Karaoke[0].End);
        Assert.Equal(new MediaTime(12), result.Karaoke[1].Start);
        Assert.Equal(new MediaTime(13), result.Karaoke[1].End);
        Assert.Equal(line.Start, result.Start);
        Assert.Equal(line.End, result.End);
        Assert.Equal(document.Layers, expanded.Layers);
        editor.SetKaraokeClipDuration(line.Id, line.Karaoke[0].Id, new(1));
        Assert.Equal(line.Karaoke.ToArray(), editor.Snapshot.Subtitles[0].Karaoke.ToArray());
        Assert.True(editor.Undo());
        Assert.Same(expanded, editor.Snapshot);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
    }

    private static ProjectDocument LegacyDocument(string text)
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(10), new(11), text);
        var line = editor.Snapshot.Subtitles[0] with
        {
            InlineSpans = [new(0, text.Length, new() { Bold = true })],
            Karaoke = [new(0, text.Length, new(1, 3), new(7, 3), new(1, 0, 0))
            {
                HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
            }],
            KaraokeStyleSpans = [new(0, text.Length, new() { Fill = new(0, 1, 0), StrokeWidth = 4 }, new() { StrokeWidth = 0 })]
        };
        return editor.Snapshot with { Subtitles = [line with { Id = id }] };
    }
}
