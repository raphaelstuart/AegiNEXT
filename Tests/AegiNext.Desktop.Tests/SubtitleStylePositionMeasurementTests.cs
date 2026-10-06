using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Rendering;
using AegiNext.Desktop.Settings.Styles;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleStylePositionMeasurementTests
{
    [Fact]
    public void EmbeddedFontMeasurementsUseActualInkAndCurrentCanvasWithoutWritingAssets()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "NotoSans.ttf"));
        var font = new EmbeddedSubtitleFont("Sample.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes)), [.. bytes]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Example", new()
        {
            FontFamily = "Embedded test font", FontSize = 22, Margin = 8,
            StrokeWidth = 0, ShadowColor = SceneColor.Transparent
        }, font);

        var measured = SubtitleStylePositionMeasurer.Measure(preset, 256, 160, "  ABC  \n  DEF  ");
        var plain = SubtitleStylePositionMeasurer.Measure(preset, 256, 160, "ABC\nDEF");
        var largerCanvas = SubtitleStylePositionMeasurer.Measure(preset, 1920, 1080, "  ABC  \n  DEF  ");

        Assert.Null(measured.Error);
        Assert.NotNull(measured.Position);
        Assert.True(measured.Geometry!.HasInk);
        Assert.Equal(new ScenePoint(256, 160), measured.Geometry.ParentSize);
        Assert.InRange(measured.Geometry.GlyphSize.X, 20, 100);
        Assert.InRange(measured.Geometry.GlyphSize.Y, 20, 80);
        Assert.InRange(Math.Abs(plain.Geometry!.GlyphSize.X - measured.Geometry.GlyphSize.X), 0, 0.001);
        Assert.InRange(Math.Abs(plain.Geometry.GlyphSize.Y - measured.Geometry.GlyphSize.Y), 0, 0.001);
        Assert.Equal(new ScenePoint(1920, 1080), largerCanvas.Geometry!.ParentSize);
        Assert.InRange(Math.Abs(measured.Geometry.GlyphSize.X - largerCanvas.Geometry.GlyphSize.X), 0, 0.001);
        Assert.InRange(Math.Abs(measured.Geometry.GlyphSize.Y - largerCanvas.Geometry.GlyphSize.Y), 0, 0.001);
        Assert.Null(preset.Style.FontAssetId);
        Assert.Null(preset.Style.Position);
        Assert.Same(font, preset.Font);
    }

    [Fact]
    public void InvalidFontReportsADiagnosticAndFontRepairRestoresPresetSelection()
    {
        var font = new EmbeddedSubtitleFont("Broken.ttf", Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])), [1, 2, 3]);
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Broken example", new() { FontFamily = "Broken embedded face" }, font);
        var model = new StyleSettingsViewModel();
        model.SetPositionMeasurement(value => SubtitleStylePositionMeasurer.Measure(value, 320, 180, "Subtitle Preview"));
        model.UpdateStyles([preset]);

        Assert.True(model.HasPositionMeasurementError);
        Assert.False(string.IsNullOrWhiteSpace(model.PositionMeasurementError));
        Assert.False(model.Position.CanSelectPreset);
        Assert.Same(font, model.Draft!.Font);
        Assert.Null(model.Draft.Style.Position);

        model.CommitFont("sans-serif");

        Assert.False(model.HasPositionMeasurementError);
        Assert.Null(model.PositionMeasurementError);
        Assert.Null(model.Draft.Font);
        Assert.True(model.Position.CanSelectPreset);
        Assert.NotNull(model.Position.Geometry);
        Assert.True(model.Position.SelectPreset(new(0.5, 0.5)));
        Assert.Equal(new ScenePoint(0.5, 0.5), model.Draft.Style.Position!.Anchor);
    }

    [Fact]
    public void CorruptProjectFontDisablesMeasuredPlacementAndRepairRestoresItWithoutChangingTheSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"aegi-position-font-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "broken.ttf"), [1, 2, 3]);
            var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "broken.ttf");
            var subtitle = new SubtitleLine
            {
                Text = "ABC", Style = new() { FontAssetId = font.Id, FontFamily = "Broken face", Position = new() }
            };
            var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = subtitle.Id, End = subtitle.End };
            var document = new ProjectDocument { Assets = [font], Subtitles = [subtitle], Layers = [layer] };
            using var resolver = new LayerPlacementResolver();

            var failed = resolver.Resolve(document, directory, layer);

            Assert.IsType<InvalidDataException>(failed.Error);
            Assert.Null(failed.BasePosition);
            Assert.Null(failed.Geometry);
            Assert.Equal(font.Id, document.Subtitles[0].Style.FontAssetId);
            Assert.Same(subtitle.Style.Position, document.Subtitles[0].Style.Position);
            var repaired = document with
            {
                Subtitles = [subtitle with { Style = subtitle.Style with { FontAssetId = null, FontFamily = "sans-serif" } }]
            };
            var measured = resolver.Resolve(repaired, directory, layer);
            Assert.Null(measured.Error);
            Assert.NotNull(measured.BasePosition);
            Assert.True(measured.Geometry!.HasInk);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
