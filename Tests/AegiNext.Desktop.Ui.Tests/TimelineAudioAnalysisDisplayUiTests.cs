using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Media.Analysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineAudioAnalysisDisplayUiTests
{
    [AvaloniaFact]
    public void GainChangesRenderedEnvelopeWithoutRebuildingSpectrumOrMovingViewport()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = new SubtitleTimelineControl { IsSpectrumVisible = false };
        timeline.SetDocument(new(), null, null);
        timeline.SetAudioGraphPalette(new() { Waveform = "#FF0000FF" });
        var waveform = new WaveformData(new(MediaTime.Zero, 65536, 10),
            Enumerable.Range(0, 10).SelectMany(_ => new[] { -0.25f, 0.25f }).ToArray());
        timeline.SetWaveform(waveform, null, new(10));
        var window = CreateWindow(timeline);
        try
        {
            window.UpdateLayout();
            timeline.PixelsPerSecond = 30;
            var viewport = timeline.Viewport;
            var builds = timeline.SpectrumBitmapBuildCount;
            var sample = new Point(timeline.HeaderWidth + 2.5 * timeline.PixelsPerSecond,
                timeline.RulerHeight + timeline.Viewport.Height * 0.32);
            using var original = Capture(timeline);
            Assert.False(IsRed(Pixel(original, sample)));
            timeline.SetAudioAnalysisDisplay(new() { WaveformGain = 2 });
            using var amplified = Capture(timeline);
            Assert.True(IsRed(Pixel(amplified, sample)));
            Assert.Equal(builds, timeline.SpectrumBitmapBuildCount);
            timeline.SetAudioAnalysisDisplay(new());
            using var restored = Capture(timeline);
            Assert.Equal(Pixel(original, sample), Pixel(restored, sample));
            Assert.Equal(viewport, timeline.Viewport);
            Assert.Equal(MediaTime.Zero, timeline.Position);
            Assert.Equal(-0.25f, waveform.Peaks.Span[0]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void BrightnessAndContrastRemapCachedEnergyAndPreserveSilenceAndLayerIdentity()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = new SubtitleTimelineControl { IsWaveformVisible = false };
        timeline.SetDocument(new(), null, null);
        timeline.SetAudioGraphPalette(new() { Low = "#000000", Mid = "#808080", High = "#FFFFFF" });
        var spectrum = new SpectrogramData(4, 1, new(10), [0, 64, 128, 255], []);
        var overview = new SpectrogramData(4, 1, new(10), [0, 64, 128, 255], []);
        timeline.SetSpectrogram(spectrum, overview);
        var window = CreateWindow(timeline);
        try
        {
            window.UpdateLayout();
            timeline.PixelsPerSecond = (timeline.Bounds.Width - timeline.HeaderWidth) / 10;
            var silent = new Point(timeline.HeaderWidth + 0.5 * timeline.PixelsPerSecond,
                timeline.RulerHeight + timeline.Viewport.Height * 0.25);
            var medium = new Point(timeline.HeaderWidth + 6.25 * timeline.PixelsPerSecond, silent.Y);
            var builds = timeline.SpectrumBitmapBuildCount;
            var overviewBuilds = timeline.SpectrumOverviewBitmapBuildCount;
            using var original = Capture(timeline);
            timeline.SetAudioAnalysisDisplay(new() { SpectrumBrightness = 1.5 });
            using var bright = Capture(timeline);
            Assert.True(Pixel(bright, medium).Red > Pixel(original, medium).Red);
            Assert.Equal(Pixel(original, silent), Pixel(bright, silent));
            timeline.SetAudioAnalysisDisplay(new() { SpectrumContrast = 2 });
            using var contrast = Capture(timeline);
            Assert.True(Pixel(contrast, medium).Red < Pixel(original, medium).Red);
            Assert.Equal(Pixel(original, silent), Pixel(contrast, silent));
            Assert.Equal(builds + 2, timeline.SpectrumBitmapBuildCount);
            Assert.Equal(overviewBuilds + 2, timeline.SpectrumOverviewBitmapBuildCount);
            timeline.SetAudioAnalysisDisplay(new() { SpectrumContrast = 2 });
            timeline.SetSpectrogram(spectrum, overview);
            Assert.Equal(builds + 2, timeline.SpectrumBitmapBuildCount);
            Assert.Equal(overviewBuilds + 2, timeline.SpectrumOverviewBitmapBuildCount);
            Assert.Equal((byte)128, spectrum.Levels.Span[2]);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task SharedDisplayPreferencesReachTimelineWithoutChangingDocumentOrAnalysis()
    {
        await using var context = new MainWindowTestContext();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var document = context.Session.DocumentSnapshot;
        var viewport = timeline.Viewport;
        var spectrum = context.ViewModel.Timeline.Spectrogram;
        var builds = timeline.SpectrumBitmapBuildCount;
        context.Session.UpdatePreferences(value => value with
        {
            AudioAnalysis = value.AudioAnalysis with { Display = new() { WaveformGain = 2, SpectrumBrightness = 2 } }
        });
        Assert.Equal(builds + 1, timeline.SpectrumBitmapBuildCount);
        Assert.Same(spectrum, context.ViewModel.Timeline.Spectrogram);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.Equal(viewport, timeline.Viewport);
    }

    private static Window CreateWindow(SubtitleTimelineControl timeline)
    {
        var window = new Window
        {
            Width = 520, Height = 220, Content = timeline, RequestedThemeVariant = ThemeVariant.Dark
        };
        window.Show();
        return window;
    }

    private static SKBitmap Capture(SubtitleTimelineControl timeline)
    {
        using var target = new RenderTargetBitmap(new((int)timeline.Bounds.Width, (int)timeline.Bounds.Height), new(96, 96));
        target.Render(timeline);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static SKColor Pixel(SKBitmap bitmap, Point point) => bitmap.GetPixel((int)point.X, (int)point.Y);
    private static bool IsRed(SKColor color) => color.Red > 180 && color.Green < 80 && color.Blue < 80;
}
