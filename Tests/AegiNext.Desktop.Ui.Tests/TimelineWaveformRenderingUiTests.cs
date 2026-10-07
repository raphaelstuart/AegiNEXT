using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
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

public sealed class TimelineWaveformRenderingUiTests
{
    private static readonly double[] quietSpectrumTimes = [12.07, 12.17, 12.27, 12.37, 12.47, 13.07, 13.17, 13.27, 13.37, 13.47];
    private static readonly double[] peakSpectrumTimes = [12.57, 12.67, 12.77, 12.87, 12.97];

    /// <summary>独立频谱没有峰值时不会生成旧式波形回退。</summary>
    [AvaloniaFact]
    public void LevelsOnlySpectrumDoesNotInventWaveformPeaks()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        timeline.SetWaveform(null, null, new(2));
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 150), 60);
            using var empty = Capture(timeline);
            var levelsOnly = new SpectrogramData(4, 128, MediaTime.Zero, new(1, 2), new byte[4 * 128]);
            Assert.True(levelsOnly.Waveform.IsEmpty);
            timeline.SetSpectrogram(levelsOnly);
            using var spectrum = Capture(timeline);
            var y = WaveY(timeline, 0.2);
            for (var x = TimeX(timeline, 0.05); x < TimeX(timeline, 1.95); x++)
            {
                Assert.Equal(empty.GetPixel(x, y), spectrum.GetPixel(x, y));
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LocalSpectrumUsesItsOwnStartAndColumnIntervalsOverTheOverview()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        timeline.IsWaveformVisible = false;
        timeline.IsSpectrumVisible = true;
        var overview = new SpectrogramData(16, 128, new(10), new(1, 2), new byte[16 * 128]);
        var levels = new byte[4 * 128];
        for (var row = 0; row < 128; row++)
        {
            levels[row * 4 + 1] = 255;
        }
        var detail = new SpectrogramData(4, 128, new(12), new(1, 2), levels);
        timeline.SetSpectrogram(null, overview);
        timeline.SetWaveform(null, null, new(18));
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(10, 50), 60);
            var y = WaveY(timeline, 0.1);
            var beforeX = TimeX(timeline, 11.27);
            var peakX = TimeX(timeline, 12.77);
            var afterX = TimeX(timeline, 14.27);
            using var coarse = Capture(timeline);
            timeline.SetSpectrogram(detail, overview);
            using var refined = Capture(timeline);
            Assert.Equal(coarse.GetPixel(beforeX, y), refined.GetPixel(beforeX, y));
            Assert.All(quietSpectrumTimes, time =>
                Assert.Equal(coarse.GetPixel(TimeX(timeline, time), y), refined.GetPixel(TimeX(timeline, time), y)));
            Assert.All(peakSpectrumTimes, time =>
                Assert.NotEqual(coarse.GetPixel(TimeX(timeline, time), y), refined.GetPixel(TimeX(timeline, time), y)));
            Assert.Equal(coarse.GetPixel(afterX, y), refined.GetPixel(afterX, y));
            timeline.SetSpectrogram(null, overview);
            using var restored = Capture(timeline);
            Assert.Equal(coarse.GetPixel(peakX, y), restored.GetPixel(peakX, y));
            Save(refined, "timeline-spectrum-local-detail.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EnlargingTwoCoarseBucketsKeepsTheVisibleEnvelopeContinuous()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var overview = ConstantWaveform(MediaTime.Zero, 32768, 2, -0.8f, 0.8f);
        timeline.SetWaveform(null, overview, overview.End);
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 600), 60);
            using var image = Capture(timeline);
            var y = WaveY(timeline, 0.2);
            var first = (int)Math.Ceiling(timeline.HeaderWidth) + 3;
            var last = (int)timeline.Bounds.Width - 3;
            var redColumns = Enumerable.Range(first, last - first).Count(x => IsRed(image.GetPixel(x, y)));
            Assert.True(redColumns >= (last - first) * 0.95,
                $"放大后的峰值包络只覆盖 {redColumns}/{last - first} 个像素列。");
            Save(image, "timeline-waveform-coarse-enlarged.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LocalDetailReplacesOnlyItsOwnTimeRangeAndFallsBackWhenCleared()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var overview = ConstantWaveform(MediaTime.Zero, 8192, 16, -0.8f, 0.8f);
        var detail = ConstantWaveform(new(32768, WaveformAnalyzer.SAMPLE_RATE), 1024, 32, -0.1f, 0.1f);
        timeline.SetWaveform(null, overview, overview.End);
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 150), 60);
            var y = WaveY(timeline, 0.2);
            var beforeX = TimeX(timeline, Seconds(detail.Start) - 0.13);
            var detailX = TimeX(timeline, Seconds(detail.Start + detail.Duration / 2));
            var afterX = TimeX(timeline, Seconds(detail.End) + 0.13);
            using var coarse = Capture(timeline);
            Assert.True(IsRed(coarse.GetPixel(detailX, y)));

            timeline.SetWaveform(detail, overview, overview.End);
            using var refined = Capture(timeline);
            Assert.True(IsRed(refined.GetPixel(beforeX, y)));
            Assert.False(IsRed(refined.GetPixel(detailX, y)));
            Assert.True(IsRed(refined.GetPixel(afterX, y)));
            Assert.True(IsRed(refined.GetPixel(detailX, WaveY(timeline, 0.02))));

            timeline.SetWaveform(null, overview, overview.End);
            using var restored = Capture(timeline);
            Assert.Equal(coarse.GetPixel(detailX, y), restored.GetPixel(detailX, y));
            Save(refined, "timeline-waveform-local-detail.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ShrinkingPreservesSeparatePositiveAndNegativeTransientPeaksInOnePixel()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 1, 4096);
        var peaks = new float[request.BucketCount * 2];
        peaks[1500 * 2] = -0.95f;
        peaks[1501 * 2 + 1] = 0.95f;
        var detail = new WaveformData(request, peaks);
        timeline.SetWaveform(detail, null, detail.End);
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 100), 60);
            using var image = Capture(timeline);
            var x = TimeX(timeline, 1500d / WaveformAnalyzer.SAMPLE_RATE);
            Assert.True(IsRed(image.GetPixel(x, WaveY(timeline, 0.3))));
            Assert.True(IsRed(image.GetPixel(x, WaveY(timeline, -0.3))));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DensePeaksDoNotAccumulateWaveformOpacityWhenTheySharePixels()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        timeline.SetAudioGraphPalette(new() { Waveform = "#FF000080" });
        var overview = ConstantWaveform(MediaTime.Zero, 1024, 64, -0.9f, 0.9f);
        var detail = ConstantWaveform(MediaTime.Zero, 1, WaveformAnalysisRequest.MAX_BUCKET_COUNT, -0.9f, 0.9f);
        timeline.SetWaveform(null, overview, new(1));
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 10), 60);
            using var coarse = Capture(timeline);
            timeline.SetWaveform(detail, overview, new(1));
            using var dense = Capture(timeline);
            var y = WaveY(timeline, 0.2);
            for (var x = (int)timeline.HeaderWidth + 1; x <= (int)timeline.HeaderWidth + 2; x++)
            {
                Assert.Equal(coarse.GetPixel(x, y), dense.GetPixel(x, y));
                Assert.InRange(dense.GetPixel(x, y).Red, 110, 180);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EnvelopeStopsAtTheAudioEndEvenWhenTheCachedBucketExtendsFurther()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var overview = ConstantWaveform(MediaTime.Zero, 8192, 16, -0.8f, 0.8f);
        timeline.SetWaveform(null, overview, new(1, 3));
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 200), 60);
            using var image = Capture(timeline);
            var y = WaveY(timeline, 0.2);
            Assert.True(IsRed(image.GetPixel(TimeX(timeline, 1d / 3 - 0.02), y)));
            for (var x = TimeX(timeline, 1d / 3 + 0.02); x < timeline.Bounds.Width - 2; x++)
            {
                Assert.False(IsRed(image.GetPixel(x, y)), $"音频结束后仍在像素 {x} 绘制波形。");
            }

            timeline.SetWaveform(null, overview, MediaTime.Zero);
            using var empty = Capture(timeline);
            Assert.False(IsRed(empty.GetPixel(TimeX(timeline, 0.2), y)));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ControlWheelChangesEnvelopeProjectionWithoutSeekingOrEditingTheDocument()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var cue = new SubtitleLine { Start = new(1), End = new(3), Text = "TEST 中文 123" };
        var document = new ProjectDocument
        {
            Subtitles = [cue],
            Layers = [new() { Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End }]
        };
        timeline.SetDocument(document, null, null);
        timeline.Position = new(4);
        var overview = ConstantWaveform(MediaTime.Zero, 8192, 64, -0.8f, 0.8f);
        timeline.SetWaveform(null, overview, overview.End);
        var seeks = 0;
        var edits = 0;
        timeline.SeekRequested += (_, _) => seeks++;
        timeline.TimingChanged += (_, _) => edits++;
        timeline.ClipsMoveCompleted += (_, _) => edits++;
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(2, 100), 60);
            var wheelPoint = new Point(timeline.HeaderWidth + 100, timeline.Bounds.Height * 0.75);
            var anchorTime = timeline.ViewStart + 100 / timeline.PixelsPerSecond;
            var originalScale = timeline.PixelsPerSecond;
            window.MouseWheel(wheelPoint, new(0, 8), RawInputModifiers.Control);
            Assert.True(timeline.PixelsPerSecond > originalScale);
            Assert.Equal(anchorTime, timeline.ViewStart + 100 / timeline.PixelsPerSecond, 8);
            using var enlarged = Capture(timeline);
            var first = (int)timeline.HeaderWidth + 5;
            var last = (int)timeline.Bounds.Width - 5;
            var y = WaveY(timeline, 0.2);
            Assert.True(Enumerable.Range(first, last - first).Count(x => IsRed(enlarged.GetPixel(x, y))) >
                (last - first) * 0.95);
            window.MouseWheel(wheelPoint, new(0, -8), RawInputModifiers.Control);
            Assert.Equal(originalScale, timeline.PixelsPerSecond, 8);
            Assert.Equal(0, seeks);
            Assert.Equal(0, edits);
            Assert.Equal(new MediaTime(4), timeline.Position);
            Assert.Same(cue, Assert.Single(document.Subtitles));
            Assert.Equal(new MediaTime(1), cue.Start);
            Assert.Equal(new MediaTime(3), cue.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IndependentWaveformRemainsAvailableWhenTheSpectrumLayerIsHidden()
    {
        using var environment = new UiTestEnvironment();
        using var timeline = CreateTimeline();
        var overview = ConstantWaveform(MediaTime.Zero, 8192, 64, -0.8f, 0.8f);
        timeline.SetWaveform(null, overview, overview.End);
        var window = CreateWindow(timeline);
        try
        {
            Prepare(window);
            timeline.SetViewport(new(0, 100), 60);
            var x = TimeX(timeline, 0.75);
            var y = WaveY(timeline, 0.2);
            using var waveformOnly = Capture(timeline);
            Assert.True(IsRed(waveformOnly.GetPixel(x, y)));
            timeline.IsWaveformVisible = false;
            using var hidden = Capture(timeline);
            Assert.False(IsRed(hidden.GetPixel(x, y)));
            timeline.IsSpectrumVisible = true;
            using var noSpectrumData = Capture(timeline);
            Assert.Equal(hidden.GetPixel(x, y), noSpectrumData.GetPixel(x, y));
            timeline.IsWaveformVisible = true;
            using var restored = Capture(timeline);
            Assert.Equal(waveformOnly.GetPixel(x, y), restored.GetPixel(x, y));
        }
        finally
        {
            window.Close();
        }
    }

    private static SubtitleTimelineControl CreateTimeline()
    {
        var timeline = new SubtitleTimelineControl { IsSpectrumVisible = false };
        timeline.SetAudioGraphPalette(new() { Waveform = "#FF0000FF" });
        timeline.SetDocument(new(), null, null);
        return timeline;
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

    private static WaveformData ConstantWaveform(MediaTime start, int samplesPerBucket, int bucketCount, float minimum, float maximum)
    {
        var request = new WaveformAnalysisRequest(start, samplesPerBucket, bucketCount);
        var peaks = new float[bucketCount * 2];
        for (var index = 0; index < bucketCount; index++)
        {
            peaks[index * 2] = minimum;
            peaks[index * 2 + 1] = maximum;
        }

        return new(request, peaks);
    }

    private static void Prepare(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static int TimeX(SubtitleTimelineControl timeline, double time) =>
        (int)Math.Floor(timeline.HeaderWidth + (time - timeline.ViewStart) * timeline.PixelsPerSecond);

    private static int WaveY(SubtitleTimelineControl timeline, double offset) =>
        (int)Math.Floor(timeline.RulerHeight + timeline.Viewport.Height * (0.5 - offset));

    private static double Seconds(MediaTime value) => (double)value.Numerator / value.Denominator;

    private static bool IsRed(SKColor color) => color.Red > 180 && color.Green < 80 && color.Blue < 80;

    private static SKBitmap Capture(SubtitleTimelineControl timeline)
    {
        using var target = new RenderTargetBitmap(new((int)timeline.Bounds.Width, (int)timeline.Bounds.Height), new(96, 96));
        target.Render(timeline);
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
