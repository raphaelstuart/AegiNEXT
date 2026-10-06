using AegiNext.Core.Projects;

namespace AegiNext.Core.Tests.Projects;

public sealed class KaraokeHighlightStyleTests
{
    [Fact]
    public void HighlightSnapshotKeepsHdrVisualsAndExcludesTypographyAndFontResources()
    {
        var style = new SubtitleStyle
        {
            FontAssetId = Guid.NewGuid(), FontSize = 250, Fill = new(4, -2, 8, 0.4),
            Stroke = new(0, 3, 1), StrokeWidth = 5, ShadowColor = new(1, 0, 4, 0.7),
            ShadowOffset = new(-7, 11), ShadowBlur = 3
        };
        var highlight = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Highlight", style);
        var line = new SubtitleLine { Text = "ab", KaraokeStyle = highlight, Karaoke = [new(0, 2, new(0), new(1), SceneColor.White)] };
        ProjectValidator.Validate(Document(line));
        Assert.Equal(style.Fill, highlight.Fill);
        Assert.Equal(style.ShadowOffset, highlight.ShadowOffset);
        Assert.True(highlight.VisuallyEquals(highlight with { PresetId = Guid.NewGuid(), PresetName = "Renamed" }));
        Assert.False(highlight.VisuallyEquals(highlight with { StrokeWidth = 6 }));
        Assert.Null(line.Style.FontAssetId);
        Assert.Equal(64, line.Style.FontSize);
    }

    [Theory]
    [InlineData("id")]
    [InlineData("name")]
    [InlineData("alpha")]
    [InlineData("fill")]
    [InlineData("stroke")]
    [InlineData("shadow")]
    [InlineData("offset")]
    public void InvalidHighlightSnapshotIsRejected(string field)
    {
        var style = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Highlight", new());
        style = field switch
        {
            "id" => style with { PresetId = Guid.Empty },
            "name" => style with { PresetName = "" },
            "alpha" => style with { Fill = new(1, 0, 0, 1.1) },
            "fill" => style with { Fill = new(double.NaN, 0, 0) },
            "stroke" => style with { StrokeWidth = -1 },
            "shadow" => style with { ShadowBlur = 513 },
            "offset" => style with { ShadowOffset = new(double.PositiveInfinity, 0) },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.Throws<InvalidDataException>(() => ProjectValidator.Validate(Document(new() { KaraokeStyle = style })));
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
