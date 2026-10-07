using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineDrawingCacheScalingUiTests
{
    [AvaloniaTheory]
    [InlineData(1d, false)]
    [InlineData(1.5d, false)]
    [InlineData(2d, false)]
    [InlineData(1d, true)]
    [InlineData(1.5d, true)]
    [InlineData(2d, true)]
    public void CachedTextAndTransformedGeometryMatchDirectDrawingAfterRepeatedClipping(double scaling, bool fractionalSize)
    {
        using var environment = new UiTestEnvironment();
        using var cache = new TimelineDrawingCache();
        var size = fractionalSize ? new Size(240.25, 104.25) : new Size(240, 104);
        var owner = new Control();
        var builds = 0;
        void Draw(DrawingContext context)
        {
            builds++;
            using var textOptions = context.PushTextOptions(new() { TextRenderingMode = TextRenderingMode.Antialias });
            for (var index = 0; index < 3; index++)
            {
                var top = index * 32;
                using var clip = context.PushClip(new Rect(0, top, size.Width, 32));
                context.DrawRectangle(Brushes.Navy, null, new(0, top, size.Width, 32));
                using var layout = WorkbenchTextFormatting.CreateLayout(owner, "字幕 ABC 123", 11, Brushes.White, 20);
                layout.Draw(context, new(6, top + 4));
                using var transform = context.PushTransform(Matrix.CreateTranslation(190, top + 10));
                context.DrawRectangle(Brushes.Lime, null, new(0, 0, 8, 8));
            }
        }

        using var direct = Capture(size, scaling, context => cache.Draw(context, size, scaling, false, Draw));
        using var cached = Capture(size, scaling, context => cache.Draw(context, size, scaling, true, Draw));
        AssertEquivalent(direct, cached, new(size), scaling);
        Assert.Equal(2, builds);
        Assert.Equal((long)Math.Ceiling(size.Width * scaling) * (long)Math.Ceiling(size.Height * scaling) * 4,
            cache.AllocatedBytes);
        using var reused = Capture(size, scaling, context => cache.Draw(context, size, scaling, true, Draw));
        AssertEquivalent(cached, reused, new(size), scaling);
        Assert.Equal(2, builds);
        cache.Dispose();
        Assert.Equal(0, cache.AllocatedBytes);
    }

    [AvaloniaTheory]
    [InlineData(1d, false)]
    [InlineData(1.5d, false)]
    [InlineData(2d, false)]
    [InlineData(1d, true)]
    [InlineData(1.5d, true)]
    [InlineData(2d, true)]
    public void TimelineLabelsKeepTheirPixelsWhenResizingAcrossTheCacheBudget(double scaling, bool dark)
    {
        using var environment = new UiTestEnvironment();
        var firstTrack = SubtitleTrack.Default with { Name = "中文 ABC 123", StylePresetName = "Style 中文" };
        var secondTrack = new SubtitleTrack { Name = "日本語 ABC 456", StylePresetName = "Style 日本語" };
        var firstCue = new SubtitleLine { TrackId = firstTrack.Id, Start = new(1), End = new(4), Text = "字幕 ABC 123" };
        var secondCue = new SubtitleLine { TrackId = secondTrack.Id, Start = new(1), End = new(4), Text = "字幕 DEF 456" };
        var firstLayer = new ProjectLayer
        {
            SubtitleId = firstCue.Id, Kind = LayerKind.SUBTITLE, Start = firstCue.Start, End = firstCue.End
        };
        var secondLayer = new ProjectLayer
        {
            SubtitleId = secondCue.Id, Kind = LayerKind.SUBTITLE, Start = secondCue.Start, End = secondCue.End,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.2), new(new(2), 0.8)])]
        };
        var document = new ProjectDocument
        {
            SubtitleTracks = [firstTrack, secondTrack], Subtitles = [firstCue, secondCue], Layers = [firstLayer, secondLayer]
        };
        using var timeline = new SubtitleTimelineControl { IsWaveformVisible = false, IsSpectrumVisible = false };
        timeline.SetDocument(document, secondCue.Id, secondLayer, trackId: secondTrack.Id);
        var window = new Window
        {
            Width = 800, Height = 260, Content = timeline,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            window.SetRenderScaling(scaling);
            Flush(window);
            timeline.SetViewport(new(0, 80), 120);
            Assert.True(TimelineDrawingCache.CanCache(timeline.Bounds.Size, scaling, 5));
            using var small = Capture(timeline.Bounds.Size, scaling, timeline.Render);
            SaveCapture(small, $"timeline-cache-small-{scaling}x-{(dark ? "dark" : "light")}.png");
            Assert.InRange(timeline.CachedDrawingBytes, 1, TimelineDrawingCache.MAX_CONTROL_BYTES);
            var regions = new[]
            {
                new Rect(0, 0, 700, 24),
                timeline.GetTrackHeaderRectangle(firstTrack.Id)!.Value,
                timeline.GetTrackHeaderRectangle(secondTrack.Id)!.Value,
                new Rect(timeline.HeaderWidth, timeline.GetTrackHeaderRectangle(secondTrack.Id)!.Value.Top, 180, 26),
                timeline.GetClipRectangle(firstLayer.Id)!.Value,
                timeline.GetClipRectangle(secondLayer.Id)!.Value
            };

            window.Width = 2000;
            window.Height = 900;
            Flush(window);
            Assert.False(TimelineDrawingCache.CanCache(timeline.Bounds.Size, scaling, 5));
            using var large = Capture(timeline.Bounds.Size, scaling, timeline.Render);
            SaveCapture(large, $"timeline-cache-large-{scaling}x-{(dark ? "dark" : "light")}.png");
            Assert.Equal(0, timeline.CachedDrawingBytes);
            foreach (var region in regions)
            {
                AssertEquivalent(large, small, region, scaling);
            }

            window.Width = 800;
            window.Height = 260;
            Flush(window);
            using var restored = Capture(timeline.Bounds.Size, scaling, timeline.Render);
            SaveCapture(restored, $"timeline-cache-restored-{scaling}x-{(dark ? "dark" : "light")}.png");
            Assert.InRange(timeline.CachedDrawingBytes, 1, TimelineDrawingCache.MAX_CONTROL_BYTES);
            foreach (var region in regions)
            {
                AssertEquivalent(large, restored, region, scaling);
            }
        }
        finally
        {
            window.Close();
        }
    }

    private static SKBitmap Capture(Size size, double scaling, Action<DrawingContext> draw)
    {
        var pixels = new PixelSize((int)Math.Ceiling(size.Width * scaling), (int)Math.Ceiling(size.Height * scaling));
        using var target = new RenderTargetBitmap(pixels);
        using (var context = target.CreateDrawingContext())
        {
            using var transform = context.PushTransform(Matrix.CreateScale(scaling, scaling));
            draw(context);
        }
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void SaveCapture(SKBitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        Directory.CreateDirectory(directory);
        using var image = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(Path.Combine(directory, name));
        image.SaveTo(stream);
    }

    private static void AssertEquivalent(SKBitmap expected, SKBitmap actual, Rect region, double scaling)
    {
        var left = (int)Math.Ceiling(region.Left * scaling);
        var top = (int)Math.Ceiling(region.Top * scaling);
        var right = Math.Min(actual.Width, (int)Math.Floor(region.Right * scaling));
        var bottom = Math.Min(actual.Height, (int)Math.Floor(region.Bottom * scaling));
        Assert.True(left >= 0 && top >= 0 && right > left && bottom > top, $"Invalid comparison region: {region}.");
        Assert.InRange(right, 1, expected.Width);
        Assert.InRange(bottom, 1, expected.Height);
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var expectedPixel = expected.GetPixel(x, y);
                var actualPixel = actual.GetPixel(x, y);
                var difference = Math.Max(Math.Abs(expectedPixel.Alpha - actualPixel.Alpha),
                    Math.Max(Math.Abs(expectedPixel.Red - actualPixel.Red),
                        Math.Max(Math.Abs(expectedPixel.Green - actualPixel.Green), Math.Abs(expectedPixel.Blue - actualPixel.Blue))));
                Assert.True(difference <= 3, $"Pixel ({x}, {y}) at {scaling}x: expected {expectedPixel}, actual {actualPixel}.");
            }
        }
    }
}
