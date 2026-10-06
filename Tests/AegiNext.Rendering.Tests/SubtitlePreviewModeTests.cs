using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class SubtitlePreviewModeTests
{
    [Fact]
    public void NormalModePreservesOrdinaryInlineAppearanceAtEveryPlaybackTime()
    {
        var document = Document();
        var line = document.Subtitles[0];
        using var renderer = Renderer();
        var plain = line with { Karaoke = [] };
        var expected = Pixels(renderer, document, plain, new(20), SubtitlePreviewMode.TIMED);
        Assert.Equal(expected, Pixels(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.NORMAL));
        Assert.Equal(expected, Pixels(renderer, document, line, new(20), SubtitlePreviewMode.NORMAL));
        Assert.NotEqual(expected, Pixels(renderer, document, line, new(20), SubtitlePreviewMode.TIMED));
    }

    [Fact]
    public void HighlightedModeActivatesOnlyTimedTextWithPerCharacterOverridesAndKeepsGeometry()
    {
        var document = Document();
        var line = document.Subtitles[0];
        using var renderer = Renderer();
        var before = renderer.MeasureSubtitleTextLayout(document, line);
        var expected = Pixels(renderer, document, line, new(20), SubtitlePreviewMode.TIMED);
        Assert.Equal(expected, Pixels(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED));
        Assert.Equal(expected, Pixels(renderer, document, line, new(20), SubtitlePreviewMode.HIGHLIGHTED));
        var after = renderer.MeasureSubtitleTextLayout(document, line);
        Assert.Equal(before.Bounds, after.Bounds);
        Assert.Equal(before.Graphemes, after.Graphemes);
        Assert.Equal(before.Runs, after.Runs);
        Assert.Equal("AB\nC", line.Text);
        Assert.Equal(2, line.Karaoke.Length);
    }

    [Fact]
    public void LegacyHighlightedModeReplacesFillWithoutDrawingTheOrdinaryFillUnderneath()
    {
        var document = Document();
        var line = document.Subtitles[0] with
        {
            Text = "AB", InlineSpans = [], KaraokeStyle = null,
            Style = document.Subtitles[0].Style with { StrokeWidth = 0, ShadowColor = SceneColor.Transparent },
            Karaoke = [new(0, 2, new(10), new(11), new(1, 0, 0, 0.5))]
        };
        using var renderer = Renderer();
        var reference = line with { Karaoke = [], Style = line.Style with { Fill = new(1, 0, 0, 0.5) } };
        Assert.Equal(Pixels(renderer, document, reference, MediaTime.Zero, SubtitlePreviewMode.NORMAL),
            Pixels(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED));
    }

    [Fact]
    public void AHighlightSnapshotDoesNotRecolorTextWithoutCharacterTiming()
    {
        var document = Document();
        var line = document.Subtitles[0] with { Karaoke = [] };
        using var renderer = Renderer();
        Assert.Equal(Pixels(renderer, document, line, new(20), SubtitlePreviewMode.NORMAL),
            Pixels(renderer, document, line, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED));
    }

    private static byte[] Pixels(ProjectSceneRenderer renderer, ProjectDocument document, SubtitleLine line,
        MediaTime time, SubtitlePreviewMode mode)
    {
        using var surface = renderer.RenderSubtitlePreview(document, line, time, new SKRect(-100, -100, 300, 250), mode);
        return surface.CopySrgbBgra();
    }

    private static ProjectSceneRenderer Renderer() => new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));

    private static ProjectDocument Document()
    {
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "Fixtures/NotoSans.ttf");
        var line = new SubtitleLine
        {
            Text = "AB\nC", End = new(20),
            Style = new() { FontAssetId = font.Id, FontSize = 28, Fill = new(0, 0, 1), StrokeWidth = 1 },
            InlineSpans = [new(1, 1, new() { Bold = true, Fill = new(0, 1, 0) })],
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Red", new()
            {
                Fill = new(1, 0, 0), StrokeWidth = 3, ShadowOffset = new(-5, 4), ShadowColor = new(1, 0, 1, 0.5)
            }),
            Karaoke = [new(0, 1, new(10), new(11), SceneColor.White),
                new(1, 1, new(11), new(12), SceneColor.White)
                {
                    ActiveStyle = new() { Stroke = new(0, 1, 0), StrokeWidth = 5 },
                    InactiveStyle = new() { Fill = SceneColor.Transparent }, HighlightKind = KaraokeHighlightKind.OUTLINE_STEP
                }]
        };
        return new() { Width = 300, Height = 200, Assets = [font], Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
