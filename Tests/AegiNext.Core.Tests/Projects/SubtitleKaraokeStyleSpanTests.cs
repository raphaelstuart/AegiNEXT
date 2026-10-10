using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class SubtitleKaraokeStyleSpanTests
{
    [Fact]
    public void UntimedRangesValidateAndResolveOnlyTheirSelectedVisualState()
    {
        var line = new SubtitleLine
        {
            Text = "Ae\u0301B",
            KaraokeStyleSpans = [new(1, 2, new() { Fill = SceneColor.Transparent, StrokeWidth = 0 },
                new() { ShadowOffset = new(3, -4) })]
        };
        ProjectValidator.ValidateSubtitleKaraoke(line);
        ProjectValidator.Validate(Document(line));
        Assert.Null(KaraokeVisualStyleResolver.RangeStyleAt(line, 0, KaraokeVisualState.ACTIVE));
        Assert.Equal(SceneColor.Transparent, KaraokeVisualStyleResolver.RangeStyleAt(line, 1, KaraokeVisualState.ACTIVE)!.Fill);
        Assert.Equal(new ScenePoint(3, -4), KaraokeVisualStyleResolver.RangeStyleAt(line, 1, KaraokeVisualState.INACTIVE)!.ShadowOffset);
        Assert.Empty(line.Karaoke);
    }

    [Fact]
    public void RangeOverridesWinActiveDefaultsWhileOutlineModeKeepsItsInactiveRule()
    {
        var ordinary = new SubtitleStyle { FontSize = 90, Bold = true, Fill = SceneColor.Black, StrokeWidth = 3 };
        var group = new KaraokeSegment(0, 2, new(0), new(1), SceneColor.White)
        {
            HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
        };
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Default", ordinary with { Fill = new(4, 0, 2) });
        var active = KaraokeVisualStyleResolver.ResolveActive(ordinary, highlight, group,
            new() { Fill = SceneColor.Transparent, StrokeWidth = 0 });
        var inactive = KaraokeVisualStyleResolver.ResolveInactive(ordinary, group,
            new() { Fill = SceneColor.Transparent, StrokeWidth = 9 });
        Assert.Equal(SceneColor.Transparent, active.Fill);
        Assert.Equal(0, active.StrokeWidth);
        Assert.Equal(90, active.FontSize);
        Assert.True(active.Bold);
        Assert.Equal(SceneColor.Transparent, inactive.Fill);
        Assert.Equal(0, inactive.StrokeWidth);
        Assert.Equal(9, KaraokeVisualStyleResolver.ResolveInactive(ordinary,
            group with { HighlightKind = KaraokeHighlightKind.STEP }, new() { StrokeWidth = 9 }).StrokeWidth);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void InvalidRangesAreRejectedEvenWithoutTiming(int invalid)
    {
        var style = new KaraokeVisualStyleOverride { Fill = SceneColor.Black };
        var line = new SubtitleLine { Text = "Ae\u0301B", KaraokeStyleSpans = invalid switch
        {
            0 => default,
            1 => [null!],
            2 => [new(1, 1, style)],
            3 => [new(0, 3, style), new(1, 2, style)],
            4 => [new(0, 5, style)],
            5 => [new(0, 1)],
            _ => [new(0, 1, new() { StrokeWidth = -1 })]
        } };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.ValidateSubtitleKaraoke(line));
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(line)));
    }

    private static ProjectDocument Document(SubtitleLine line)
    {
        return new() { Subtitles = [line], Layers = [new()
        {
            SubtitleId = line.Id, Start = line.Start, End = line.End
        }] };
    }
}
