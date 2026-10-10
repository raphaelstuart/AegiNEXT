using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class InactiveKaraokeSplitMergeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplitPreservesDisabledAndMixedGraphemesWithExactClocksAndStyles(bool mixed)
    {
        var clips = ImmutableArray.Create(
            Clip(0, 1, new(1, 7), new(2, 7)),
            Clip(1, 2, new(3, 7), new(5, 7)),
            Clip(3, 5, new(8, 7), new(13, 7)),
            Clip(8, 1, new(17, 7), new(20, 7)));
        var line = WithStyles(new SubtitleLine
        {
            Text = "Ae\u0301👩‍💻Z", Start = new(5), End = new(9), KaraokeStyle = Highlight(),
            Karaoke = mixed ? [clips[0], clips[2]] : [],
            InactiveKaraoke = mixed ? [clips[1], clips[3]] : clips
        });
        var original = Document(line, new(1, 3));

        var split = ProjectEditingOperations.SplitSubtitle(original, line.Id, new(7), 3);

        var left = split.Subtitles[0];
        var right = split.Subtitles[1];
        Assert.Equal("Ae\u0301", left.Text);
        Assert.Equal("👩‍💻Z", right.Text);
        Assert.Equal(line.Id, left.Id);
        Assert.NotEqual(line.Id, right.Id);
        Assert.Equal(line.Karaoke.Where(clip => clip.Utf16Start < 3), left.Karaoke);
        Assert.Equal(line.Karaoke.Where(clip => clip.Utf16Start >= 3)
            .Select(clip => clip with { Utf16Start = clip.Utf16Start - 3 }), right.Karaoke);
        Assert.Equal(line.InactiveKaraoke.Where(clip => clip.Utf16Start < 3), left.InactiveKaraoke);
        Assert.Equal(line.InactiveKaraoke.Where(clip => clip.Utf16Start >= 3)
            .Select(clip => clip with { Utf16Start = clip.Utf16Start - 3 }), right.InactiveKaraoke);
        Assert.Equal(line.KaraokeStyle, left.KaraokeStyle);
        Assert.Equal(line.KaraokeStyle, right.KaraokeStyle);
        Assert.Equal(new MediaTime(1, 3), split.Layers[0].AnimationOffset);
        Assert.Equal(new MediaTime(7, 3), split.Layers[1].AnimationOffset);
        Assert.Same(line, Assert.Single(original.Subtitles));
        ProjectValidator.Validate(split);
    }

    [Fact]
    public void SplitRetainsTheHighlightSnapshotOnlyOnThePieceWithSavedSegments()
    {
        var line = WithStyles(new SubtitleLine
        {
            Text = "ab", End = new(4), KaraokeStyle = Highlight(),
            InactiveKaraoke = [Clip(0, 1, new(1, 7), new(2, 7))]
        });

        var split = ProjectEditingOperations.SplitSubtitle(Document(line), line.Id, new(2), 1);

        Assert.Empty(split.Subtitles[0].Karaoke);
        Assert.Equal(line.KaraokeStyle, split.Subtitles[0].KaraokeStyle);
        Assert.Equal(line.InactiveKaraoke.ToArray(), split.Subtitles[0].InactiveKaraoke.ToArray());
        Assert.Empty(split.Subtitles[1].Karaoke);
        Assert.Empty(split.Subtitles[1].InactiveKaraoke);
        Assert.Null(split.Subtitles[1].KaraokeStyle);
    }

    [Fact]
    public void SavedWordMustBeExplicitlySplitBeforePartitioningTheSubtitle()
    {
        var clip = Clip(0, 4, new(1, 7), new(29, 7));
        var line = WithStyles(new SubtitleLine
        {
            Text = "abcd", Start = new(5), End = new(9), KaraokeStyle = Highlight(), InactiveKaraoke = [clip]
        });

        var document = Document(line, new(1, 3));
        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.SplitSubtitle(document, line.Id, new(7), 2));
        var editor = new ProjectEditor(document);
        editor.SetSubtitleKaraokeEnabled(line.Id, true);
        editor.SplitKaraokeClip(line.Id, clip.Id, 2, new(7, 3));
        editor.SetSubtitleKaraokeEnabled(line.Id, false);
        var split = ProjectEditingOperations.SplitSubtitle(editor.Snapshot, line.Id, new(7), 2);

        Assert.Single(split.Subtitles[0].InactiveKaraoke);
        Assert.Single(split.Subtitles[1].InactiveKaraoke);
        Assert.Equal(clip.Id, split.Subtitles[0].InactiveKaraoke[0].Id);
        Assert.NotEqual(clip.Id, split.Subtitles[1].InactiveKaraoke[0].Id);
        Assert.Equal(clip.Start, split.Subtitles[0].InactiveKaraoke[0].Start);
        Assert.Equal(new MediaTime(7, 3), split.Subtitles[0].InactiveKaraoke[^1].End);
        Assert.Equal(new MediaTime(7, 3), split.Subtitles[1].InactiveKaraoke[0].Start);
        Assert.Equal(clip.End, split.Subtitles[1].InactiveKaraoke[^1].End);
        foreach (var piece in split.Subtitles)
        {
            Assert.Empty(piece.Karaoke);
            Assert.Equal(line.KaraokeStyle, piece.KaraokeStyle);
            foreach (var saved in piece.InactiveKaraoke)
            {
                Assert.Equal(KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.ACTIVE),
                    KaraokeVisualStyleResolver.RangeStyleAt(piece, saved.Utf16Start, KaraokeVisualState.ACTIVE));
                Assert.Equal(KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.INACTIVE),
                    KaraokeVisualStyleResolver.RangeStyleAt(piece, saved.Utf16Start, KaraokeVisualState.INACTIVE));
                Assert.Equal(clip.HighlightColor, saved.HighlightColor);
                Assert.Equal(clip.HighlightKind, saved.HighlightKind);
            }
        }
    }

    [Fact]
    public void SplitRejectsSavedWordCrossingsOutsideTheirClockWithoutMutatingTheDocument()
    {
        var line = WithStyles(new SubtitleLine
        {
            Text = "abcd", Start = new(5), End = new(9), InactiveKaraoke = [Clip(0, 4, new(3), new(4))]
        });
        var document = Document(line);

        Assert.Throws<InvalidOperationException>(() => ProjectEditingOperations.SplitSubtitle(document, line.Id, new(7), 2));

        Assert.Same(line, Assert.Single(document.Subtitles));
        Assert.Equal(new MediaTime(3), Assert.Single(line.InactiveKaraoke).Start);
        Assert.Empty(line.Karaoke);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void MergeAndResplitKeepEachSegmentsEnablementAndExactRebasedTimes(bool firstEnabled, bool secondEnabled)
    {
        var style = Highlight();
        var firstClip = Clip(0, 1, new(1, 7), new(2, 7));
        var secondClip = Clip(0, 1, new(2, 7), new(5, 7));
        var first = WithStyles(new SubtitleLine
        {
            Text = "a", Start = new(1), End = new(2), KaraokeStyle = style,
            Karaoke = firstEnabled ? [firstClip] : [], InactiveKaraoke = firstEnabled ? [] : [firstClip]
        });
        var second = WithStyles(new SubtitleLine
        {
            Text = "b", Start = new(3), End = new(4),
            KaraokeStyle = style with { PresetId = Guid.NewGuid(), PresetName = "Copy" },
            Karaoke = secondEnabled ? [secondClip] : [], InactiveKaraoke = secondEnabled ? [] : [secondClip]
        });
        var document = PairDocument(first, second, new(1, 3), new(2, 5));

        var merged = ProjectEditingOperations.MergeSubtitles(document, first.Id, second.Id);

        var result = Assert.Single(merged.Subtitles);
        var rebasedSecond = secondClip with { Utf16Start = 2, Start = new(233, 105), End = new(278, 105) };
        Assert.Equal("a\nb", result.Text);
        Assert.Equal(style, result.KaraokeStyle);
        Assert.Equal(first.Karaoke, result.Karaoke.Where(clip => clip.Id == firstClip.Id));
        Assert.Equal(first.InactiveKaraoke, result.InactiveKaraoke.Where(clip => clip.Id == firstClip.Id));
        Assert.Equal(second.Karaoke.Select(_ => rebasedSecond), result.Karaoke.Where(clip => clip.Id == secondClip.Id));
        Assert.Equal(second.InactiveKaraoke.Select(_ => rebasedSecond), result.InactiveKaraoke.Where(clip => clip.Id == secondClip.Id));
        Assert.Equal(new MediaTime(1, 3), Assert.Single(merged.Layers).AnimationOffset);

        var split = ProjectEditingOperations.SplitSubtitle(merged, result.Id, new(3), 2);
        Assert.Equal(first.Karaoke.ToArray(), split.Subtitles[0].Karaoke.ToArray());
        Assert.Equal(first.InactiveKaraoke.ToArray(), split.Subtitles[0].InactiveKaraoke.ToArray());
        var savedSecond = rebasedSecond with { Utf16Start = 0 };
        Assert.Equal(second.Karaoke.Select(_ => savedSecond), split.Subtitles[1].Karaoke);
        Assert.Equal(second.InactiveKaraoke.Select(_ => savedSecond), split.Subtitles[1].InactiveKaraoke);
        Assert.Equal(style, split.Subtitles[0].KaraokeStyle);
        Assert.Equal(style, split.Subtitles[1].KaraokeStyle);
        ProjectValidator.Validate(split);
    }

    [Fact]
    public void MergeInheritsTheOnlySavedHighlightSnapshotWhenTheFirstLineHasNoSegments()
    {
        var first = WithStyles(new SubtitleLine { Text = "a", End = new(2) });
        var second = WithStyles(new SubtitleLine
        {
            Text = "b", Start = new(2), End = new(4), KaraokeStyle = Highlight(),
            InactiveKaraoke = [Clip(0, 1, new(1, 7), new(2, 7))]
        });

        var merged = ProjectEditingOperations.MergeSubtitles(PairDocument(first, second), first.Id, second.Id, "");

        var result = Assert.Single(merged.Subtitles);
        Assert.Empty(result.Karaoke);
        Assert.Equal(second.KaraokeStyle, result.KaraokeStyle);
        Assert.Equal(second.InactiveKaraoke[0] with { Utf16Start = 1, Start = new(15, 7), End = new(16, 7) },
            Assert.Single(result.InactiveKaraoke));
    }

    [Fact]
    public void MergeBakesConflictingLineHighlightsIntoBothActiveAndSavedSegments()
    {
        var first = WithStyles(new SubtitleLine
        {
            Text = "ab", End = new(2), KaraokeStyle = Highlight(),
            Karaoke = [Clip(0, 1, new(1, 7), new(2, 7))],
            InactiveKaraoke = [Clip(1, 1, new(3, 7), new(5, 7))]
        });
        var second = WithStyles(new SubtitleLine
        {
            Text = "cd", Start = new(2), End = new(4),
            KaraokeStyle = Highlight() with { Fill = SceneColor.Black, StrokeWidth = 9.123456789123 },
            Karaoke = [Clip(0, 1, new(1, 7), new(2, 7))],
            InactiveKaraoke = [Clip(1, 1, new(3, 7), new(5, 7))]
        });

        var merged = ProjectEditingOperations.MergeSubtitles(PairDocument(first, second), first.Id, second.Id, "");

        var result = Assert.Single(merged.Subtitles);
        Assert.Null(result.KaraokeStyle);
        Assert.Equal(2, result.Karaoke.Length);
        Assert.Equal(2, result.InactiveKaraoke.Length);
        foreach (var source in new[] { first, second })
        {
            foreach (var segment in source.Karaoke.Concat(source.InactiveKaraoke))
            {
                var changed = result.Karaoke.Concat(result.InactiveKaraoke).Single(clip => clip.Id == segment.Id);
                Assert.Equal(KaraokeVisualStyleResolver.ResolveActive(source.Style, source.KaraokeStyle, segment,
                        KaraokeVisualStyleResolver.RangeStyleAt(source, segment.Utf16Start, KaraokeVisualState.ACTIVE)),
                    KaraokeVisualStyleResolver.ResolveActive(result.Style, result.KaraokeStyle, changed,
                        KaraokeVisualStyleResolver.RangeStyleAt(result, changed.Utf16Start, KaraokeVisualState.ACTIVE)));
                Assert.Equal(KaraokeVisualStyleResolver.RangeStyleAt(source, segment.Utf16Start, KaraokeVisualState.INACTIVE),
                    KaraokeVisualStyleResolver.RangeStyleAt(result, changed.Utf16Start, KaraokeVisualState.INACTIVE));
                Assert.Equal(segment.HighlightKind, changed.HighlightKind);
                Assert.Equal(segment.HighlightColor, changed.HighlightColor);
                var offset = source == first ? MediaTime.Zero : new MediaTime(2);
                Assert.Equal(segment.Start + offset, changed.Start);
                Assert.Equal(segment.End + offset, changed.End);
                Assert.Equal(segment.Utf16Start + (source == first ? 0 : 2), changed.Utf16Start);
            }
        }
    }

    [Fact]
    public void MergeDeduplicatesIdsAcrossTheCompleteActiveAndSavedCollections()
    {
        var firstActive = Clip(0, 1, new(0), new(1));
        var firstSaved = Clip(1, 1, new(1), new(2));
        var first = WithStyles(new SubtitleLine
        {
            Text = "ab", End = new(2), Karaoke = [firstActive], InactiveKaraoke = [firstSaved]
        });
        var second = WithStyles(new SubtitleLine
        {
            Text = "cd", Start = new(2), End = new(4),
            Karaoke = [Clip(0, 1, new(0), new(1)) with { Id = firstSaved.Id }],
            InactiveKaraoke = [Clip(1, 1, new(1), new(2)) with { Id = firstActive.Id }]
        });

        var merged = ProjectEditingOperations.MergeSubtitles(PairDocument(first, second), first.Id, second.Id, "");

        var line = Assert.Single(merged.Subtitles);
        Assert.Equal(firstActive.Id, line.Karaoke[0].Id);
        Assert.Equal(firstSaved.Id, line.InactiveKaraoke[0].Id);
        Assert.NotEqual(firstSaved.Id, line.Karaoke[1].Id);
        Assert.NotEqual(firstActive.Id, line.InactiveKaraoke[1].Id);
        Assert.Equal(4, line.Karaoke.Concat(line.InactiveKaraoke).Select(clip => clip.Id).Distinct().Count());
        Assert.Equal(new MediaTime(2), line.Karaoke[1].Start);
        Assert.Equal(new MediaTime(4), line.InactiveKaraoke[1].End);
        ProjectValidator.Validate(merged);
    }

    private static SubtitleLine WithStyles(SubtitleLine line)
    {
        return line with { KaraokeStyleSpans = line.Karaoke.Concat(line.InactiveKaraoke)
            .OrderBy(clip => clip.Utf16Start).Select(clip => new SubtitleKaraokeStyleSpan(clip.Utf16Start, clip.Utf16Length,
                new() { ShadowOffset = new(3.123456789123, -2.234567891234) },
                new() { Fill = SceneColor.Transparent, StrokeWidth = 0 })).ToImmutableArray() };
    }

    private static KaraokeSegment Clip(int start, int length, MediaTime begin, MediaTime end)
    {
        return new(start, length, begin, end, new(4.123456789123, -0.2, 3.234567891234, 0.5))
        {
            HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
        };
    }

    private static KaraokeHighlightStyle Highlight()
    {
        return KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Saved HDR highlight", new()
        {
            Fill = new(5.123456789123, 0.25, 3, 0.75), Stroke = new(0, 1, 0), StrokeWidth = 2.234567891234,
            ShadowOffset = new(6, -4), ShadowColor = new(0, 0, 2, 0.6), ShadowBlur = 5.123456789123
        });
    }

    private static ProjectDocument Document(SubtitleLine line, MediaTime animationOffset = default)
    {
        return new()
        {
            Subtitles = [line], Layers = [new()
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id,
                Start = line.Start, End = line.End, AnimationOffset = animationOffset
            }]
        };
    }

    private static ProjectDocument PairDocument(SubtitleLine first, SubtitleLine second,
        MediaTime firstOffset = default, MediaTime secondOffset = default)
    {
        return new()
        {
            Subtitles = [first, second], Layers =
            [new() { Id = first.Id, Kind = LayerKind.SUBTITLE, SubtitleId = first.Id,
                Start = first.Start, End = first.End, AnimationOffset = firstOffset },
                new() { Id = second.Id, Kind = LayerKind.SUBTITLE, SubtitleId = second.Id,
                    Start = second.Start, End = second.End, AnimationOffset = secondOffset }]
        };
    }
}
