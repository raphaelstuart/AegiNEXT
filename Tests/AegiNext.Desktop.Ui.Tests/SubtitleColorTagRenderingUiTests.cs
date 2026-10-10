using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleColorTagRenderingUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void TagHueSurvivesSelectionAndSnapshotChangesInvalidateClipsWithoutRebuildingAudio(bool dark)
    {
        using var environment = new UiTestEnvironment();
        var tag = new SubtitleColorTag { Name = "Review", ColorHex = "#E63655" };
        var cue = new SubtitleLine { Start = new(1), End = new(3), Text = "ABC 字幕", ColorTagId = tag.Id };
        var layer = new ProjectLayer { SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
        var document = new ProjectDocument { ColorTags = [tag], Subtitles = [cue], Layers = [layer] };
        using var timeline = new SubtitleTimelineControl { IsSpectrumVisible = false, IsWaveformVisible = false };
        timeline.SetClipPalette(new() { AdaptToTheme = false, SelectedClip = "#2244FF", InactiveClip = "#777777" });
        timeline.SetDocument(document, null, null);
        var window = new Window { Width = 800, Height = 260, Content = timeline,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            Flush(window);
            var rectangle = timeline.GetClipRectangle(layer.Id)!.Value;
            using var inactive = Capture(timeline);
            var normal = FillPixel(inactive, rectangle);
            Assert.True(normal.Red > normal.Green + 15);

            timeline.SetDocument(document, cue.Id, layer, [layer.Id]);
            using var selected = Capture(timeline);
            var active = FillPixel(selected, rectangle);
            Assert.True(active.Red > active.Green + 15);
            Assert.NotEqual(normal, active);
            var spectrumBuilds = timeline.SpectrumBitmapBuildCount;
            var changed = document with { ColorTags = [tag with { ColorHex = "#2255FF" }] };
            timeline.SetDocument(changed, cue.Id, layer, [layer.Id]);
            using var changedImage = Capture(timeline);
            var blue = FillPixel(changedImage, rectangle);
            Assert.True(blue.Blue > blue.Red + 15);
            Assert.Equal(spectrumBuilds, timeline.SpectrumBitmapBuildCount);

            var builds = timeline.StaticDrawingBuildCount;
            for (var frame = 0; frame < 6; frame++)
            {
                timeline.Position = new(frame, 10);
                using var playback = Capture(timeline);
                Assert.Equal(blue, FillPixel(playback, rectangle));
            }
            Assert.Equal(builds, timeline.StaticDrawingBuildCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InvalidDragOverridesTheTagFillAndCancellationRestoresItsColor()
    {
        using var environment = new UiTestEnvironment();
        var tag = new SubtitleColorTag { Name = "Ready", ColorHex = "#00AAFF" };
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "First", ColorTagId = tag.Id };
        var second = new SubtitleLine { Start = new(4), End = new(6), Text = "Second" };
        var layers = new[]
        {
            new ProjectLayer { SubtitleId = first.Id, Start = first.Start, End = first.End },
            new ProjectLayer { SubtitleId = second.Id, Start = second.Start, End = second.End }
        };
        var document = new ProjectDocument { ColorTags = [tag], Subtitles = [first, second], Layers = [.. layers] };
        using var timeline = new SubtitleTimelineControl { IsSnapEnabled = false, PixelsPerSecond = 60,
            IsSpectrumVisible = false, IsWaveformVisible = false };
        timeline.SetDocument(document, first.Id, layers[0], [layers[0].Id]);
        timeline.ClipSelectionChanged += (_, e) => e.SelectionAccepted = true;
        var window = new Window { Width = 800, Height = 260, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            Flush(window);
            var rectangle = timeline.GetClipRectangle(layers[0].Id)!.Value;
            using var original = Capture(timeline);
            var tagged = FillPixel(original, rectangle);
            var destination = rectangle.Center + new Vector(timeline.PixelsPerSecond * 2, 0);
            window.MouseDown(rectangle.Center, MouseButton.Left);
            window.MouseMove(destination);
            Assert.True(timeline.HasActiveDrag);
            var moved = timeline.GetClipRectangle(layers[0].Id)!.Value;
            using var invalid = Capture(timeline);
            var invalidColor = FillPixel(invalid, moved);
            Assert.True(invalidColor.Red > invalidColor.Blue);

            timeline.CancelGesture();
            window.MouseUp(destination, MouseButton.Left);
            using var restored = Capture(timeline);
            Assert.Equal(tagged, FillPixel(restored, rectangle));
            Assert.Equal(new(1), first.Start);
        }
        finally
        {
            window.Close();
        }
    }

    private static SKColor FillPixel(SKBitmap image, Rect rectangle) =>
        image.GetPixel((int)rectangle.Right - 10, (int)rectangle.Bottom - 5);

    private static SKBitmap Capture(Control control)
    {
        using var target = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        target.Render(control);
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
}
