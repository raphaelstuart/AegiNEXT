using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTimingPreviewRenderingUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiveEndMovesTheRenderedClipFillBoundaryAndOverviewWhileTheDocumentStaysFixed(bool dark)
    {
        using var environment = new UiTestEnvironment();
        var cue = new SubtitleLine { Text = "打轴 Timing 123", Start = new(1), End = new(1001, 1000) };
        var layer = new ProjectLayer
        {
            Id = cue.Id, SubtitleId = cue.Id, Kind = LayerKind.SUBTITLE, Start = cue.Start, End = cue.End
        };
        var document = new ProjectDocument { Subtitles = [cue], Layers = [layer] };
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSpectrumVisible = false, IsWaveformVisible = false };
        var overview = new TimelineOverviewControl { Height = 28 };
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(overview, Avalonia.Controls.Dock.Top);
        panel.Children.Add(overview);
        panel.Children.Add(timeline);
        var window = new Window
        {
            Width = 640, Height = 280, Content = panel,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            timeline.SetDocument(document, cue.Id, layer, [layer.Id]);
            timeline.SetViewport(new(0, 80), 10);
            timeline.SetClipPalette(new()
            {
                AdaptToTheme = false, SelectedClip = "#2266DD", StartLine = "#00FF00", EndLine = "#FF0000",
                SelectedRangeFill = "#2266DD80"
            });
            overview.SetScene(document, timeline.Viewport, 10, MediaTime.Zero);
            var original = timeline.GetClipRectangle(layer.Id)!.Value;
            using var before = Capture(timeline);
            using var overviewBefore = Capture(overview);
            var preview = new TimelineTimingPreview(cue.Id, cue.Start, new(7, 2));
            timeline.SetTimingPreview(preview);
            overview.SetScene(document, timeline.Viewport, 10, MediaTime.Zero, preview);
            var rectangle = timeline.GetClipRectangle(layer.Id)!.Value;
            Assert.Equal(original.Left, rectangle.Left);
            Assert.Equal(timeline.HeaderWidth + 280, rectangle.Right, 6);
            Assert.Equal(new MediaTime(1001, 1000), Assert.Single(document.Subtitles).End);
            Assert.Equal(new MediaTime(1001, 1000), Assert.Single(document.Layers).End);
            using var after = Capture(timeline);
            var bottom = (int)timeline.Bounds.Height - 8;
            var fillX = (int)timeline.HeaderWidth + 167;
            Assert.NotEqual(before.GetPixel(fillX, bottom), after.GetPixel(fillX, bottom));
            Assert.Contains(Enumerable.Range(-2, 5), offset =>
            {
                var pixel = after.GetPixel((int)rectangle.Right + offset, bottom);
                return pixel.Red > pixel.Green + 80 && pixel.Red > pixel.Blue + 80;
            });
            using var overviewAfter = Capture(overview);
            Assert.NotEqual(overviewBefore.GetPixel(135, 6), overviewAfter.GetPixel(135, 6));
            Save(after, $"timing-follow-{(dark ? "dark" : "light")}.png");
            Save(overviewAfter, $"timing-follow-overview-{(dark ? "dark" : "light")}.png");
            timeline.SetTimingPreview(null);
            overview.SetScene(document, timeline.Viewport, 10, MediaTime.Zero);
            Assert.Equal(original, timeline.GetClipRectangle(layer.Id));
            using var restored = Capture(timeline);
            Assert.Equal(before.GetPixel(fillX, bottom), restored.GetPixel(fillX, bottom));
        }
        finally
        {
            window.Close();
        }
    }

    private static SKBitmap Capture(Control control)
    {
        using var target = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        target.Render(control);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static void Save(SKBitmap bitmap, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var encoded = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(directory, name));
            encoded.SaveTo(stream);
        }
    }
}
