using System.Text.Json;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleContentModelTests
{
    [Fact]
    public void ContentKindIsDerivedAndKaraokeIncludesRichText()
    {
        var line = new SubtitleLine { Text = "ab" };
        Assert.Equal(SubtitleContentKind.PLAIN, line.ContentKind);
        line = line with { InlineSpans = [new(0, 1, new() { Bold = true })] };
        Assert.Equal(SubtitleContentKind.RICH_TEXT, line.ContentKind);
        line = line with { Karaoke = [new(0, 1, new(0), new(1), SceneColor.White)] };
        Assert.Equal(SubtitleContentKind.KARAOKE, line.ContentKind);
        Assert.DoesNotContain("ContentKind", JsonSerializer.Serialize(line), StringComparison.Ordinal);
        Assert.Equal(SubtitleContentKind.RICH_TEXT, (line with
        {
            Karaoke = [], InlineSpans = [new(0, 1, new() { Bold = false })]
        }).ContentKind);
    }

    [Fact]
    public void OverridesDistinguishInheritanceFalseTransparentAndClearedFont()
    {
        var style = new SubtitleStyle { Bold = true, FontAssetId = Guid.NewGuid() };
        Assert.Equal(style, new SubtitleInlineStyleOverride().ApplyTo(style));
        var resolved = new SubtitleInlineStyleOverride
        {
            Bold = false, Fill = SceneColor.Transparent, StrokeWidth = 0, ClearFontAsset = true,
            Underline = true, Strikethrough = true
        }.ApplyTo(style);
        Assert.False(resolved.Bold);
        Assert.Null(resolved.FontAssetId);
        Assert.Equal(SceneColor.Transparent, resolved.Fill);
        Assert.Equal(0, resolved.StrokeWidth);
        Assert.True(resolved.Underline);
        Assert.True(resolved.Strikethrough);
        Assert.Null(new SubtitleInlineStyleOverride { FontFamily = "serif" }.ApplyTo(style).FontAssetId);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 5)]
    public void InlineRangesMustSelectWholeGraphemes(int start, int length)
    {
        var line = new SubtitleLine { Text = "😀e\u0301", InlineSpans = [new(start, length, new() { Italic = true })] };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line)));
    }

    [Fact]
    public void InlineRangesRejectOverlapEmptyStylesAndInvalidFontReferences()
    {
        var line = new SubtitleLine { Text = "abcd", InlineSpans = [new(0, 2, new() { Bold = true })] };
        ProjectValidator.Validate(Document(line));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InlineSpans = line.InlineSpans.Add(new(1, 2, new() { Italic = true }))
        })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { InlineSpans = [new(0, 1, new())] })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InlineSpans = [new(0, 1, new() { FontAssetId = Guid.NewGuid() })]
        })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InlineSpans = [new(0, 1, new() { FontSize = double.NaN })]
        })));
    }

    [Fact]
    public void KaraokeIdentityModesAndVisualOverridesAreValidatedWithoutCroppingOldTimes()
    {
        var segment = new KaraokeSegment(0, 1, new(0), new(4), SceneColor.White)
        {
            HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
        };
        Assert.NotEqual(Guid.Empty, segment.Id);
        Assert.Equal(segment.Id, (segment with { End = new(5) }).Id);
        var line = new SubtitleLine
        {
            Text = "ab", Karaoke = [segment],
            KaraokeStyleSpans = [new(0, 1, new() { StrokeWidth = 0 }, new() { Fill = new(4, -0.1, 2, 0.5) })]
        };
        ProjectValidator.Validate(Document(line));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            Karaoke = [segment, segment with { Utf16Start = 1 }]
        })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { Karaoke = [segment with { Id = Guid.Empty }] })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            Karaoke = [segment with { HighlightKind = (KaraokeHighlightKind)100 }]
        })));
    }

    [Fact]
    public void InactiveKaraokeDoesNotEnableHighlightAndMixedRangesRemainIndependent()
    {
        var first = new KaraokeSegment(0, 1, new(0), new(4), SceneColor.White);
        var third = new KaraokeSegment(2, 1, new(5), new(7), SceneColor.White);
        var line = new SubtitleLine
        {
            Text = "abcd",
            InactiveKaraoke = [first, third],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Saved", new() { Fill = new(4, 0, 0) })
        };
        Assert.Equal(SubtitleContentKind.PLAIN, line.ContentKind);
        ProjectValidator.Validate(Document(line));
        line = line with { InlineSpans = [new(0, 1, new() { Bold = true })] };
        Assert.Equal(SubtitleContentKind.RICH_TEXT, line.ContentKind);
        line = line with
        {
            Karaoke = [new(1, 1, new(0), new(1), SceneColor.White), new(3, 1, new(2), new(3), SceneColor.White)]
        };
        Assert.Equal(SubtitleContentKind.KARAOKE, line.ContentKind);
        ProjectValidator.Validate(Document(line));
        ProjectValidator.ValidateSubtitleKaraoke(line);
        Assert.Same(first, line.InactiveKaraoke[0]);
        Assert.Same(third, line.InactiveKaraoke[1]);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(0, 0)]
    [InlineData(0, 5)]
    [InlineData(-1, 1)]
    public void InactiveKaraokeRequiresCompleteNonemptyGraphemeRanges(int start, int length)
    {
        var line = new SubtitleLine
        {
            Text = "😀e\u0301",
            InactiveKaraoke = [new(start, length, new(0), new(1), SceneColor.White)]
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line)));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleKaraoke(line));
    }

    [Fact]
    public void ActiveAndInactiveKaraokeRejectSharedIdentityAndOverlappingTextRanges()
    {
        var active = new KaraokeSegment(1, 2, new(0), new(1), SceneColor.White);
        var line = new SubtitleLine { Text = "abcd", Karaoke = [active] };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InactiveKaraoke = [active with { Utf16Start = 3, Utf16Length = 1 }]
        })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InactiveKaraoke = [active with { Id = Guid.NewGuid(), Utf16Start = 0 }]
        })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InactiveKaraoke = [active with { Id = Guid.NewGuid(), Utf16Start = 2 }]
        })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InactiveKaraoke = [active with { Id = Guid.NewGuid() }]
        })));
    }

    [Fact]
    public void InactiveKaraokeRejectsDefaultNullUnsortedAndDuplicateSegments()
    {
        var first = new KaraokeSegment(0, 1, new(0), new(1), SceneColor.White);
        var second = new KaraokeSegment(1, 1, new(1), new(2), SceneColor.White);
        var line = new SubtitleLine { Text = "ab" };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { InactiveKaraoke = default })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { InactiveKaraoke = [null!] })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { InactiveKaraoke = [second, first] })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InactiveKaraoke = [first, second with { Id = first.Id }]
        })));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void InactiveKaraokeRejectsInvalidClockModesAndVisualValues(int invalidValue)
    {
        var segment = new KaraokeSegment(0, 1, new(0), new(4), SceneColor.White);
        segment = invalidValue switch
        {
            0 => segment with { Id = Guid.Empty },
            1 => segment with { Start = new(-1) },
            2 => segment with { End = segment.Start },
            3 => segment with { HighlightKind = (KaraokeHighlightKind)100 },
            4 => segment with { HighlightColor = new(double.NaN, 0, 0) },
            _ => segment
        };
        var line = new SubtitleLine
        {
            Text = "a", InactiveKaraoke = [segment],
            KaraokeStyleSpans = invalidValue == 5 ? [new(0, 1, new() { StrokeWidth = -1 })] : []
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line)));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleKaraoke(line));
    }

    [Fact]
    public void MissingCollectionsNullStylesAndConflictingFontOverridesAreRejected()
    {
        var line = new SubtitleLine { Text = "ab" };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { InlineSpans = default })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { InlineSpans = [null!] })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with { InlineSpans = [new(0, 1, null!)] })));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line with
        {
            InlineSpans = [new(0, 1, new() { FontAssetId = Guid.NewGuid(), ClearFontAsset = true })]
        })));
    }

    [Theory]
    [InlineData(KaraokeHighlightKind.STEP)]
    [InlineData(KaraokeHighlightKind.SWEEP)]
    [InlineData(KaraokeHighlightKind.OUTLINE_STEP)]
    public void EveryKaraokeModeSupportsVisualOverridesWithoutChangingGeometry(KaraokeHighlightKind kind)
    {
        var segment = new KaraokeSegment(0, 1, new(0), new(1), SceneColor.White)
        {
            HighlightKind = kind
        };
        var line = new SubtitleLine
        {
            Text = "a", Style = new() { FontSize = 100, Bold = true }, Karaoke = [segment],
            KaraokeStyleSpans = [new(0, 1, new() { Fill = SceneColor.Transparent, StrokeWidth = 0 })]
        };
        ProjectValidator.Validate(Document(line));
        var resolved = KaraokeVisualStyleResolver.ResolveActive(line.Style, null, segment,
            KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.ACTIVE));
        Assert.Equal(100, resolved.FontSize);
        Assert.True(resolved.Bold);
        Assert.Equal(SceneColor.Transparent, resolved.Fill);
        Assert.Equal(0, resolved.StrokeWidth);
    }

    private static ProjectDocument Document(SubtitleLine line)
    {
        return new()
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
