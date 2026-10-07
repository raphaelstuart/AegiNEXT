using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineWaveformAnalysisWorkflowUiTests
{
    [AvaloniaFact]
    public async Task RealWaveAnalyzesThroughThePanelAndWheelZoomRequestsFinerDataWithoutSeekingPlayback()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("AEGINEXT_RUN_AUDIO_TESTS") == "1",
            "真实音频分析需设置 AEGINEXT_RUN_AUDIO_TESTS=1 并准备同配置原生音频库。");
        var path = Path.Combine(Path.GetTempPath(), $"aeginext-waveform-workflow-{Guid.NewGuid():N}.wav");
        try
        {
            WriteWave(path);
            var playback = new UiAuditionAudioSource();
            var output = new UiAuditionAudioOutput();
            await using var context = new MainWindowTestContext(
                (_, _, initial, _) => Task.FromResult(new AudioPlaybackSession(playback, output, initial)),
                mediaProbe: (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 0,
                    VideoWidth: 1, VideoHeight: 1)));
            var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
            var model = context.ViewModel.Timeline;
            model.PixelsPerSecond = 10;
            await context.Window.OpenMediaAsync(path, false);
            await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.NotNull(model.Waveform);
            Assert.Null(model.WaveformOverview);
            Assert.NotNull(model.Spectrogram);
            Assert.Null(model.SpectrogramOverview);
            Assert.Equal(new MediaTime(20), model.AudioDuration);
            Assert.Equal(string.Empty, model.AnalysisStatus);
            var previousResolution = model.Waveform.SamplesPerBucket;
            var previousPosition = model.Position;
            var previousDocument = context.Session.DocumentSnapshot;
            var playbackSeeks = playback.SeekCount;
            var previousOverview = model.WaveformOverview;
            var previousSpectrumOverview = model.SpectrogramOverview;
            var previousSpectrumStep = model.Spectrogram.ColumnDuration;
            var pointer = new Point(timeline.HeaderWidth + (10 - timeline.ViewStart) * timeline.PixelsPerSecond,
                timeline.RulerHeight + timeline.Viewport.Height * 0.75);
            var windowPointer = timeline.TranslatePoint(pointer, context.Window)!.Value;

            context.Window.MouseWheel(windowPointer, new(0, 20), RawInputModifiers.Control);
            await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            Assert.NotNull(model.Waveform);
            Assert.True(model.Waveform.SamplesPerBucket < previousResolution);
            Assert.Same(previousOverview, model.WaveformOverview);
            Assert.Same(previousSpectrumOverview, model.SpectrogramOverview);
            Assert.NotNull(model.Spectrogram);
            Assert.True(model.Spectrogram.ColumnDuration < previousSpectrumStep);
            Assert.Equal(new MediaTime(SpectrogramAnalyzer.HOP_SIZE, SpectrogramAnalyzer.SAMPLE_RATE), model.Spectrogram.ColumnDuration);
            Assert.Equal(previousPosition, model.Position);
            Assert.Equal(playbackSeeks, playback.SeekCount);
            Assert.Same(previousDocument, context.Session.DocumentSnapshot);
            Assert.True(model.Waveform.Peaks.ToArray().Max() > 0.45F);
            Assert.True(timeline.ViewStart + timeline.Viewport.VisibleDuration <= 20,
                $"缩放后的视口 [{timeline.ViewStart}, {timeline.ViewStart + timeline.Viewport.VisibleDuration}) 必须位于真实音频范围 [0, 20) 内。");
            model.IsSpectrumVisible = false;
            await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            timeline.SetAudioGraphPalette(new() { Waveform = "#FF0000FF" });
            using var image = Capture(timeline);
            var y = (int)(timeline.RulerHeight + timeline.Viewport.Height * 0.4);
            var first = (int)Math.Ceiling(timeline.HeaderWidth) + 3;
            var last = (int)timeline.Bounds.Width - 3;
            Save(image);
            var colored = Enumerable.Range(first, last - first).Count(x => image.GetPixel(x, y).Red > 180 && image.GetPixel(x, y).Green < 80);
            Assert.True(colored >= (last - first) * 0.95,
                $"有效音频视口内只有 {colored}/{last - first} 个像素列绘制了波形。");

            await context.Session.Analysis.ClearAsync();
            Assert.Null(model.Waveform);
            Assert.Null(model.WaveformOverview);
            Assert.Null(model.Spectrogram);
            Assert.Null(model.SpectrogramOverview);
            Assert.Equal(MediaTime.Zero, model.AudioDuration);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void WriteWave(string path)
    {
        var sampleCount = WaveformAnalyzer.SAMPLE_RATE * 20;
        using var writer = new BinaryWriter(File.Create(path));
        writer.Write("RIFF"u8);
        writer.Write(36 + sampleCount * sizeof(short));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(WaveformAnalyzer.SAMPLE_RATE);
        writer.Write(WaveformAnalyzer.SAMPLE_RATE * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(sampleCount * sizeof(short));
        for (var index = 0; index < sampleCount; index++)
        {
            writer.Write((short)(16000 * Math.Sin(index * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE)));
        }
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

    private static void Save(SKBitmap image)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
            using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
            using var stream = File.Create(Path.Combine(directory, "timeline-waveform-real-wav-zoomed.png"));
            encoded.SaveTo(stream);
        }
    }
}
