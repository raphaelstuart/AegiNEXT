using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineSoloAlignmentUiTests
{
    [AvaloniaTheory]
    [InlineData(false, "en-US", 1d)]
    [InlineData(true, "en-US", 1d)]
    [InlineData(false, "zh-CN", 1d)]
    [InlineData(true, "zh-CN", 1d)]
    [InlineData(false, "en-US", 1.5d)]
    [InlineData(true, "en-US", 1.5d)]
    [InlineData(false, "zh-CN", 1.5d)]
    [InlineData(true, "zh-CN", 1.5d)]
    public void SoloGlyphInkBoundsAndPixelCentroidAreCenteredInsideTheTwentyDipToggle(bool active, string language, double visualScale)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var document = new ProjectDocument();
        var trackId = document.Tracks[0].Id;
        using var timeline = new SubtitleTimelineControl
        {
            Width = 520, Height = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            RenderTransform = new ScaleTransform(visualScale, visualScale),
            RenderTransformOrigin = RelativePoint.TopLeft
        };
        timeline.SetDocument(document, null, null);
        timeline.SoloTrackId = active ? trackId : null;
        var host = new Grid();
        host.Children.Add(timeline);
        var window = new Window { Width = 850, Height = 340, Content = host, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            using var pixels = Capture(window);
            var toggle = Assert.IsType<Rect>(timeline.GetTrackSoloToggleRectangle(trackId));
            Assert.Equal(20, toggle.Width);
            Assert.Equal(20, toggle.Height);
            var topLeft = timeline.TranslatePoint(toggle.TopLeft, window)!.Value;
            var bottomRight = timeline.TranslatePoint(toggle.BottomRight, window)!.Value;
            var projected = new Rect(topLeft, bottomRight);
            Assert.Equal(toggle.Width * visualScale, projected.Width, 5);
            Assert.Equal(toggle.Height * visualScale, projected.Height, 5);
            var pixelScaleX = pixels.Width / window.ClientSize.Width;
            var pixelScaleY = pixels.Height / window.ClientSize.Height;
            var glyph = MeasureGlyph(pixels, projected, pixelScaleX, pixelScaleY, visualScale);
            Assert.True(glyph.PixelCount >= 8, $"Expected the actual S glyph, found only {glyph.PixelCount} foreground pixels.");
            Assert.InRange(glyph.Bounds.Width / visualScale, 2.5, 10);
            Assert.InRange(glyph.Bounds.Height / visualScale, 5, 13);
            var detail = $"active={active}, language={language}, scale={visualScale}, toggle={projected}, " +
                $"glyphBounds={glyph.Bounds}, centroid={glyph.Centroid}, pixels={glyph.PixelCount}";
            Assert.True(Math.Abs(glyph.Bounds.Center.X - projected.Center.X) / visualScale <= 1.5,
                $"The visible S ink bounds must be horizontally centered: {detail}");
            Assert.True(Math.Abs(glyph.Centroid.X - projected.Center.X) / visualScale <= 1.5,
                $"The actual S pixel mass must be horizontally centered: {detail}");
            Assert.True(Math.Abs(glyph.Bounds.Center.Y - projected.Center.Y) / visualScale <= 1.5,
                $"The visible S ink bounds must be vertically centered: {detail}");
            Assert.True(Math.Abs(glyph.Centroid.Y - projected.Center.Y) / visualScale <= 1.5,
                $"The actual S pixel mass must be vertically centered: {detail}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StyleBadgeHasAVisibleGapBelowTheEntireSoloToggle(bool active)
    {
        using var environment = new UiTestEnvironment();
        var document = WithStyleBadge(new());
        var trackId = document.Tracks[0].Id;
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(document, null, null);
        timeline.SoloTrackId = active ? trackId : null;
        var window = new Window { Width = 520, Height = 180, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            using var pixels = Capture(window);
            var toggle = Assert.IsType<Rect>(timeline.GetTrackSoloToggleRectangle(trackId));
            var badge = Assert.IsType<Rect>(timeline.GetTrackStyleBadgeRectangle(trackId));
            Assert.False(toggle.Intersects(badge), $"The style badge overlaps the Solo toggle: solo={toggle}, badge={badge}.");
            Assert.True(badge.Top - toggle.Bottom >= 2,
                $"The style badge must leave at least 2 DIP below the Solo toggle and its border: solo={toggle}, badge={badge}.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StyleBadgeCannotCoverAnyPixelOfTheSoloToggleOrItsBorder(bool active)
    {
        using var environment = new UiTestEnvironment();
        var document = new ProjectDocument();
        var trackId = document.Tracks[0].Id;
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(document, null, null);
        timeline.SoloTrackId = active ? trackId : null;
        var window = new Window { Width = 520, Height = 180, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        window.Show();
        try
        {
            using var withoutBadge = Capture(window);
            var toggle = Assert.IsType<Rect>(timeline.GetTrackSoloToggleRectangle(trackId));
            Assert.Null(timeline.GetTrackStyleBadgeRectangle(trackId));
            timeline.SetDocument(WithStyleBadge(document), null, null);
            using var withBadge = Capture(window);
            Assert.NotNull(timeline.GetTrackStyleBadgeRectangle(trackId));
            Assert.Equal(toggle, Assert.IsType<Rect>(timeline.GetTrackSoloToggleRectangle(trackId)));
            Assert.Equal(active ? trackId : null, timeline.SoloTrackId);
            var topLeft = timeline.TranslatePoint(toggle.Inflate(1).TopLeft, window)!.Value;
            var bottomRight = timeline.TranslatePoint(toggle.Inflate(1).BottomRight, window)!.Value;
            var region = new Rect(topLeft, bottomRight);
            var pixelScaleX = withoutBadge.Width / window.ClientSize.Width;
            var pixelScaleY = withoutBadge.Height / window.ClientSize.Height;
            var left = (int)Math.Floor(region.Left * pixelScaleX);
            var top = (int)Math.Floor(region.Top * pixelScaleY);
            var right = (int)Math.Ceiling(region.Right * pixelScaleX);
            var bottom = (int)Math.Ceiling(region.Bottom * pixelScaleY);
            for (var y = top; y < bottom; y++)
            {
                for (var x = left; x < right; x++)
                {
                    var expected = withoutBadge.GetPixel(x, y);
                    var actual = withBadge.GetPixel(x, y);
                    Assert.True(expected == actual,
                        $"The style badge changed a Solo toggle/border pixel at {x},{y}: " +
                        $"active={active}, expected={expected}, actual={actual}, solo={toggle}.");
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static ProjectDocument WithStyleBadge(ProjectDocument document)
    {
        var track = document.Tracks[0];
        return document with
        {
            Tracks = document.Tracks.SetItem(0, track with
            {
                StylePresetId = Guid.NewGuid(), StylePresetName = "Style 中文 ABC 123", DefaultStyle = new()
            })
        };
    }

    private static (Rect Bounds, Point Centroid, int PixelCount) MeasureGlyph(SKBitmap pixels, Rect toggle,
        double pixelScaleX, double pixelScaleY, double visualScale)
    {
        var interior = toggle.Deflate(visualScale);
        var left = Math.Max(0, (int)Math.Floor(interior.Left * pixelScaleX));
        var top = Math.Max(0, (int)Math.Floor(interior.Top * pixelScaleY));
        var right = Math.Min(pixels.Width, (int)Math.Ceiling(interior.Right * pixelScaleX));
        var bottom = Math.Min(pixels.Height, (int)Math.Ceiling(interior.Bottom * pixelScaleY));
        var background = pixels.GetPixel((int)Math.Floor(toggle.Center.X * pixelScaleX),
            (int)Math.Floor((toggle.Bottom - 3 * visualScale) * pixelScaleY));
        var minimumX = pixels.Width;
        var maximumX = -1;
        var minimumY = pixels.Height;
        var maximumY = -1;
        var weightSum = 0d;
        var weightedX = 0d;
        var weightedY = 0d;
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var pixel = pixels.GetPixel(x, y);
                if (pixel.Red < 155 || pixel.Green < 155 || pixel.Blue < 155 || pixel.Blue - pixel.Red > 55)
                {
                    continue;
                }
                var weight = Math.Max(0, pixel.Red - background.Red);
                if (weight == 0)
                {
                    continue;
                }
                count++;
                minimumX = Math.Min(minimumX, x);
                maximumX = Math.Max(maximumX, x);
                minimumY = Math.Min(minimumY, y);
                maximumY = Math.Max(maximumY, y);
                weightSum += weight;
                weightedX += (x + 0.5) / pixelScaleX * weight;
                weightedY += (y + 0.5) / pixelScaleY * weight;
            }
        }
        Assert.True(weightSum > 0, "No actual foreground S pixels were found inside the Solo toggle.");
        return (new(new Point(minimumX / pixelScaleX, minimumY / pixelScaleY),
                new Point((maximumX + 1) / pixelScaleX, (maximumY + 1) / pixelScaleY)),
            new(weightedX / weightSum, weightedY / weightSum), count);
    }

    private static SKBitmap Capture(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
