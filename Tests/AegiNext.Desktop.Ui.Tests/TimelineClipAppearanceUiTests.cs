using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using AegiNext.Media.Analysis;
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

public sealed class TimelineClipAppearanceUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DefaultRangeFillKeepsAudioVisibleAndPlayheadAboveBoundaries(bool dark, bool spectrumVisible)
    {
        using var environment = new UiTestEnvironment();
        var scene = CreateScene(dark);
        using var timeline = scene.Timeline;
        try
        {
            const int width = 160;
            const int height = 8;
            var waveform = new float[width * 2];
            for (var index = 0; index < width; index++)
            {
                var amplitude = (float)(0.18 + 0.65 * Math.Abs(Math.Sin(index * 0.18) * Math.Sin(index * 0.041)));
                waveform[index * 2] = -amplitude;
                waveform[index * 2 + 1] = amplitude;
            }
            timeline.SetSpectrogram(new(width, height, new(8), Enumerable.Repeat((byte)63, width * height).ToArray(), waveform));
            timeline.IsSpectrumVisible = spectrumVisible;
            timeline.SetClipPalette(new());
            using (var image = Capture(timeline))
            {
                var x = (int)timeline.HeaderWidth + 120;
                var y = (int)(timeline.RulerHeight + (timeline.Bounds.Height - timeline.RulerHeight) / 2);
                var wave = image.GetPixel(x, y);
                timeline.IsWaveformVisible = false;
                using var withoutWaveform = Capture(timeline);
                var background = withoutWaveform.GetPixel(x, y);
                timeline.IsWaveformVisible = true;
                Assert.NotEqual(wave, background);
                Save(image, $"timeline-clips-{(spectrumVisible ? "spectrum" : "waveform")}-{(dark ? "dark" : "light")}.png");
            }

            timeline.Position = scene.Selected.Start;
            using var overlay = Capture(timeline);
            var boundaryX = (int)timeline.GetClipRectangle(scene.Selected.Id)!.Value.Left;
            Assert.Contains(Enumerable.Range(-2, 5), offset => IsRed(overlay.GetPixel(boundaryX + offset,
                (int)timeline.Bounds.Height - 3)));
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FineBoundariesSpanTheBodyAndSelectionRemainsReadableInBothThemes(bool dark)
    {
        using var environment = new UiTestEnvironment();
        var scene = CreateScene(dark);
        using var timeline = scene.Timeline;
        try
        {
            var selected = timeline.GetClipRectangle(scene.Selected.Id)!.Value;
            var inactive = timeline.GetClipRectangle(scene.Inactive.Id)!.Value;
            using var image = Capture(timeline);
            foreach (var y in new[] { (int)timeline.RulerHeight + 5, (int)timeline.Bounds.Height - 3 })
            {
                var startPixels = Enumerable.Range(-5, 11).Select(offset => image.GetPixel((int)selected.Left + offset, y))
                    .Count(IsGreen);
                var endPixels = Enumerable.Range(-5, 11).Select(offset => image.GetPixel((int)selected.Right + offset, y))
                    .Count(IsRed);
                Assert.InRange(startPixels, 1, 3);
                Assert.InRange(endPixels, 1, 3);
                Assert.False(IsGreen(image.GetPixel((int)selected.Left, 10)));
                Assert.False(IsRed(image.GetPixel((int)selected.Right, 10)));
            }

            var activePixel = image.GetPixel((int)selected.Right - 10, (int)selected.Bottom - 5);
            var inactivePixel = image.GetPixel((int)inactive.Right - 10, (int)inactive.Bottom - 5);
            Assert.True(activePixel.Blue > activePixel.Red + 70);
            Assert.InRange(Math.Abs(inactivePixel.Red - inactivePixel.Blue), 0, 8);
            Assert.InRange(Math.Abs(inactivePixel.Red - inactivePixel.Green), 0, 8);
            var fill = image.GetPixel((int)selected.Center.X + 7, (int)timeline.Bounds.Height - 25);
            var outside = image.GetPixel((int)timeline.HeaderWidth + 37, (int)timeline.Bounds.Height - 25);
            Assert.True(Math.Max(Math.Abs(fill.Red - outside.Red),
                Math.Max(Math.Abs(fill.Green - outside.Green), Math.Abs(fill.Blue - outside.Blue))) > 10);

            timeline.SetClipPalette(new());
            using var defaults = Capture(timeline);
            Save(defaults, dark ? "timeline-clips-dark.png" : "timeline-clips-light.png");
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaFact]
    public void OverlappingClipsAndCoincidentBoundariesNeverAccumulateTheirOpacity()
    {
        using var environment = new UiTestEnvironment();
        var scene = CreateScene(true);
        using var timeline = scene.Timeline;
        try
        {
            timeline.SetClipPalette(DiagnosticPalette() with { StartLine = "#00FF0080", EndLine = "#FF000080" });
            var rectangle = timeline.GetClipRectangle(scene.Selected.Id)!.Value;
            using var before = Capture(timeline);
            var otherTrack = new SubtitleTrack { Name = "Overlapping track" };
            var duplicate = scene.Selected with { Id = Guid.NewGuid(), TrackId = otherTrack.Id };
            var duplicateLayer = Layer(duplicate);
            var document = scene.Document with
            {
                SubtitleTracks = [SubtitleTrack.Default, otherTrack],
                Subtitles = [.. scene.Document.Subtitles, duplicate],
                Layers = [.. scene.Document.Layers, duplicateLayer]
            };
            timeline.SetDocument(document, scene.Selected.Id, document.Layers[0], [scene.Selected.Id, duplicate.Id]);
            using var after = Capture(timeline);
            var bottom = (int)timeline.Bounds.Height - 15;
            foreach (var x in new[] { (int)rectangle.Left, (int)rectangle.Right, (int)rectangle.Center.X + 7 })
            {
                Assert.Equal(before.GetPixel(x, bottom), after.GetPixel(x, bottom));
            }

            timeline.SetDocument(document, duplicate.Id, duplicateLayer, [duplicate.Id]);
            using var mixed = Capture(timeline);
            Assert.Equal(after.GetPixel((int)rectangle.Center.X + 7, bottom), mixed.GetPixel((int)rectangle.Center.X + 7, bottom));
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaFact]
    public void TransparentSettingsRemoveTheirFillsAndDerivedBorders()
    {
        using var environment = new UiTestEnvironment();
        var scene = CreateScene(true);
        using var timeline = scene.Timeline;
        try
        {
            timeline.SetClipPalette(new()
            {
                AdaptToTheme = false, SelectedClip = "#11223300", InactiveClip = "#11223300",
                StartLine = "#11223300", EndLine = "#11223300",
                SelectedRangeFill = "#11223300", InactiveRangeFill = "#11223300"
            });
            var rectangle = timeline.GetClipRectangle(scene.Selected.Id)!.Value;
            using var image = Capture(timeline);
            var baseline = image.GetPixel((int)timeline.HeaderWidth + 37, (int)timeline.Bounds.Height - 15);
            foreach (var x in new[] { (int)rectangle.Left, (int)rectangle.Right, (int)rectangle.Center.X + 7 })
            {
                Assert.Equal(baseline, image.GetPixel(x, (int)timeline.Bounds.Height - 15));
            }

            Assert.Equal(baseline, image.GetPixel((int)rectangle.Left, (int)rectangle.Bottom - 3));
        }
        finally
        {
            scene.Window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void MoveAndTrimPreviewTheirBoundariesWithoutEditingTheSnapshotAndCancelRestoresThem(bool trim)
    {
        using var environment = new UiTestEnvironment();
        var scene = CreateScene(true);
        using var timeline = scene.Timeline;
        try
        {
            timeline.ClipSelectionChanged += (_, e) => e.SelectionAccepted = true;
            var rectangle = timeline.GetClipRectangle(scene.Selected.Id)!.Value;
            var origin = trim ? new Point(rectangle.Right - 1, rectangle.Center.Y) : rectangle.Center;
            var destination = origin + new Vector(timeline.PixelsPerSecond / 2, 0);
            scene.Window.MouseDown(origin, MouseButton.Left);
            scene.Window.MouseMove(destination);
            Assert.True(timeline.HasActiveDrag);
            var preview = timeline.GetClipRectangle(scene.Selected.Id)!.Value;
            using (var image = Capture(timeline))
            {
                Assert.Contains(Enumerable.Range(-2, 5), offset => IsRed(image.GetPixel((int)preview.Right + offset,
                    (int)timeline.Bounds.Height - 3)));
            }
            Assert.Equal(new MediaTime(3), scene.Document.Subtitles[0].End);
            timeline.CancelGesture();
            scene.Window.MouseUp(destination, MouseButton.Left);
            Assert.False(timeline.HasActiveDrag);
            Assert.Equal(rectangle, timeline.GetClipRectangle(scene.Selected.Id));
            using var restored = Capture(timeline);
            Assert.Contains(Enumerable.Range(-2, 5), offset => IsRed(restored.GetPixel((int)rectangle.Right + offset,
                (int)timeline.Bounds.Height - 3)));
        }
        finally
        {
            scene.Window.Close();
        }
    }

    private static (Window Window, SubtitleTimelineControl Timeline, ProjectDocument Document, SubtitleLine Selected,
        SubtitleLine Inactive) CreateScene(bool dark)
    {
        var first = new SubtitleLine { Start = new(1), End = new(3), Text = "选中 ABC 123" };
        var second = new SubtitleLine { Start = new(4), End = new(6), Text = "未选中 clip" };
        var document = new ProjectDocument { Subtitles = [first, second], Layers = [Layer(first), Layer(second)] };
        var timeline = new SubtitleTimelineControl { PixelsPerSecond = 80, IsSnapEnabled = false };
        timeline.SetClipPalette(DiagnosticPalette());
        timeline.SetDocument(document, first.Id, document.Layers[0]);
        var window = new Window { Width = 850, Height = 340, Content = timeline,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, timeline, document, first, second);
    }

    private static TimelineClipPalette DiagnosticPalette() => new()
    {
        AdaptToTheme = false, SelectedClip = "#2266DD", InactiveClip = "#777777",
        StartLine = "#00FF00", EndLine = "#FF0000", SelectedRangeFill = "#2266DD40", InactiveRangeFill = "#77777720"
    };

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, SubtitleId = cue.Id, Kind = LayerKind.SUBTITLE, Start = cue.Start, End = cue.End
    };

    private static bool IsGreen(SKColor color) => color.Green > color.Red + 80 && color.Green > color.Blue + 80;
    private static bool IsRed(SKColor color) => color.Red > color.Green + 80 && color.Red > color.Blue + 80;

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
