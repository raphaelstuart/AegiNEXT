using System.ComponentModel;
using System.Diagnostics;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Dock.Model.Controls;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证播放中的缓存缩放、独立细波形解码及双层发布顺序。</summary>
public sealed class TimelineAnalysisLayerPublicationUiTests
{
    private const int MEDIA_SECONDS = 60;

    /// <summary>独立细波形解码仍在等待时先发布缓存频谱，后续缩放拒绝旧波形结果。</summary>
    [AvaloniaFact]
    public async Task PlayingFineZoomPublishesSpectrumBeforeBlockedWaveformAndKeepsTheLatestRevision()
    {
        var playback = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        var video = new PreviewTestSource(1, 0, MEDIA_SECONDS * 1000L);
        await using var context = CreateContext(playback, output, video);
        await context.Controller.OpenAsync("layer-publication.media", TestContext.Current.CancellationToken);
        context.Session.Tick();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var model = context.ViewModel.Timeline;
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = timeline.Viewport.Width / 32 };
        var source = new UiRapidZoomAudioSource(MEDIA_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE);
        var detail = new UiRapidZoomAudioSource(MEDIA_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        detail.BeforeRead = token =>
        {
            entered.TrySetResult();
            release.Wait(token);
        };
        using var coordinator = new AnalysisCoordinator(context.Session,
            (path, _, mapping, duration, directory, options, budget) => new(_ => source, mapping, duration,
                cacheDirectory: directory, cacheIdentity: path, detailSourceFactory: _ => detail));
        try
        {
            await coordinator.StartAsync("synthetic-layer-publication.media");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            var previousWaveform = Assert.IsType<WaveformData>(model.Waveform);
            var previousSpectrum = Assert.IsType<SpectrogramData>(model.Spectrogram);
            var document = context.Session.DocumentSnapshot;
            var playbackSeeks = playback.SeekCount;
            var videoSeeks = video.SeekCount;
            var warmFrames = source.FramesRead;
            var analysisSeeks = source.SeekCount;
            var pointer = GraphPointer(context, timeline);
            context.Window.MouseWheel(pointer, new(0, 8), RawInputModifiers.Control);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var finePlan = Assert.IsType<WaveformViewportPlan>(
                WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(MEDIA_SECONDS)));
            Assert.True(finePlan.Analysis.SamplesPerBucket < 512);
            Assert.Same(previousWaveform, model.Waveform);
            Assert.NotSame(previousSpectrum, model.Spectrogram);
            AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
            Assert.False(coordinator.Completion.IsCompleted);
            await AdvancePlaybackAsync(context, output);
            ZoomOutToWholeMedia(context, timeline, pointer);
            release.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var finalPlan = Assert.IsType<WaveformViewportPlan>(
                WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(MEDIA_SECONDS)));
            Assert.Equal(finalPlan.Analysis, Assert.IsType<WaveformData>(model.Waveform).Request);
            Assert.True(model.Waveform!.SamplesPerBucket >= 512);
            AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
            Assert.Equal(warmFrames, source.FramesRead);
            Assert.Equal(analysisSeeks, source.SeekCount);
            Assert.Equal(playbackSeeks, playback.SeekCount);
            Assert.Equal(videoSeeks, video.SeekCount);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.False(model.IsPlaybackFollowEnabled);
            Assert.Equal(string.Empty, model.AnalysisStatus);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, detail.DisposeCount);
    }

    /// <summary>播放中的全片视口连续缩小只读取缓存，不读取分析解码器或 seek 播放源。</summary>
    [AvaloniaFact]
    public async Task RepeatedWholeMediaZoomWhilePlayingReadsTheCacheAndDoesNotSeekPlayback()
    {
        var playback = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        var video = new PreviewTestSource(1, 0, MEDIA_SECONDS * 1000L);
        await using var context = CreateContext(playback, output, video);
        await context.Controller.OpenAsync("cached-layer-publication.media", TestContext.Current.CancellationToken);
        context.Session.Tick();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var model = context.ViewModel.Timeline;
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = timeline.Viewport.Width / 32 };
        var source = new UiRapidZoomAudioSource(MEDIA_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session,
            (path, _, mapping, duration, directory, options, budget) => new(_ => source, mapping, duration,
                cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("synthetic-cached-layer-publication.media");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            var pointer = GraphPointer(context, timeline);
            ZoomOutToWholeMedia(context, timeline, pointer);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
            var warmFrames = source.FramesRead;
            var analysisSeeks = source.SeekCount;
            var playbackSeeks = playback.SeekCount;
            var videoSeeks = video.SeekCount;
            var document = context.Session.DocumentSnapshot;
            for (var iteration = 0; iteration < 6; iteration++)
            {
                context.Window.MouseWheel(pointer, new(0, -2), RawInputModifiers.Control);
                await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
                AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
                var plan = Assert.IsType<WaveformViewportPlan>(
                    WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(MEDIA_SECONDS)));
                Assert.Equal(plan.Analysis, Assert.IsType<WaveformData>(model.Waveform).Request);
                await AdvancePlaybackAsync(context, output);
                Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
                Assert.False(model.IsPlaybackFollowEnabled);
            }
            Assert.Equal(MEDIA_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE, warmFrames);
            Assert.Equal(warmFrames, source.FramesRead);
            Assert.Equal(analysisSeeks, source.SeekCount);
            Assert.Equal(playbackSeeks, playback.SeekCount);
            Assert.Equal(videoSeeks, video.SeekCount);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.Equal(string.Empty, model.AnalysisStatus);
        }
        finally
        {
            await coordinator.ClearAsync();
        }
        Assert.Equal(1, source.DisposeCount);
    }

    /// <summary>工程缓存重开无需解码，完整视口先发布频谱，再发布缓存波形。</summary>
    [AvaloniaFact]
    public async Task CompletedCacheReopensWithoutDecoderAndPublishesSpectrumBeforeWaveform()
    {
        var playback = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        var video = new PreviewTestSource(1, 0, MEDIA_SECONDS * 1000L);
        await using var context = CreateContext(playback, output, video);
        await context.Controller.OpenAsync("reopened-layer-publication.media", TestContext.Current.CancellationToken);
        context.Session.Tick();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var model = context.ViewModel.Timeline;
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = timeline.Viewport.Width / MEDIA_SECONDS };
        var source = new UiRapidZoomAudioSource(MEDIA_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE);
        using (var first = new AnalysisCoordinator(context.Session,
                   (path, _, mapping, duration, directory, options, budget) => new(_ => source, mapping, duration,
                       cacheDirectory: directory, cacheIdentity: path)))
        {
            await first.StartAsync("synthetic-reopened-layer-publication.media");
            await first.Completion.WaitAsync(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);
            AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
            await first.ClearAsync();
        }
        var publications = new List<string>();
        void OnModelChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(TimelinePanelViewModel.Spectrogram) && model.Spectrogram is not null)
            {
                publications.Add("Spectrum");
            }
            else if (args.PropertyName == nameof(TimelinePanelViewModel.Waveform) && model.Waveform is not null)
            {
                publications.Add("Waveform");
            }
        }
        using var reopened = new AnalysisCoordinator(context.Session,
            (path, _, mapping, duration, directory, options, budget) => new(_ => throw new InvalidOperationException("A completed cache must not open a decoder."),
                mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            model.PropertyChanged += OnModelChanged;
            await reopened.StartAsync("synthetic-reopened-layer-publication.media");
            await reopened.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Collection(publications, value => Assert.Equal("Spectrum", value), value => Assert.Equal("Waveform", value));
            Assert.NotNull(model.Waveform);
            AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
            Assert.Equal(string.Empty, model.AnalysisStatus);
            Assert.Equal(MEDIA_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        }
        finally
        {
            model.PropertyChanged -= OnModelChanged;
            await reopened.ClearAsync();
        }
        Assert.Equal(1, source.DisposeCount);
    }

    /// <summary>模式边界按物理像素密度决定，可见范围与预取范围使用相同模式。</summary>
    [Theory]
    [InlineData(1d, 1.01d, 16384, AudioAnalysisMode.EXACT)]
    [InlineData(1d, 1d, 32768, AudioAnalysisMode.PREVIEW)]
    [InlineData(1d, 0.99d, 32768, AudioAnalysisMode.PREVIEW)]
    [InlineData(2d, 1.01d, 16384, AudioAnalysisMode.EXACT)]
    [InlineData(2d, 1d, 32768, AudioAnalysisMode.PREVIEW)]
    [InlineData(2d, 0.99d, 32768, AudioAnalysisMode.PREVIEW)]
    public void PlannerSelectsTheModeAtThePhysicalResolutionBoundary(double scaling, double densityFactor, int samplesPerBucket,
        AudioAnalysisMode expectedMode)
    {
        var pixelsPerSecond = WaveformAnalyzer.SAMPLE_RATE / (32768d * scaling) * densityFactor;
        var plan = Assert.IsType<WaveformViewportPlan>(WaveformViewportPlanner.Create(
            new(10, pixelsPerSecond, Width: 800), scaling, new(7200)));
        Assert.Equal(samplesPerBucket, plan.Visible.SamplesPerBucket);
        Assert.Equal(samplesPerBucket, plan.Analysis.SamplesPerBucket);
        Assert.Equal(expectedMode, plan.Visible.Mode);
        Assert.Equal(expectedMode, plan.Analysis.Mode);
    }

    private static MainWindowTestContext CreateContext(UiAuditionAudioSource playback, UiAuditionAudioOutput output, PreviewTestSource video,
        int durationSeconds = MEDIA_SECONDS)
    {
        var context = new MainWindowTestContext(
            (_, _, initial, _) => Task.FromResult(new AudioPlaybackSession(playback, output, initial)),
            videoSourceFactory: () => video,
            mediaProbe: (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(durationSeconds), 1,
                VideoWidth: 1, VideoHeight: 1)));
        context.Window.Width = 1464;
        context.Window.Height = 650;
        context.Window.SetRenderScaling(2);
        var pane = Assert.IsAssignableFrom<IToolDock>(context.Window.Layouts.PanelAdapters[WorkbenchPanelIds.TIMELINE].Owner);
        var split = Assert.IsAssignableFrom<IProportionalDock>(pane.Owner);
        Assert.NotNull(split.VisibleDockables);
        var siblings = split.VisibleDockables.Where(value => value is not ISplitter && !ReferenceEquals(value, pane)).ToArray();
        var remaining = siblings.Sum(value => value.Proportion);
        foreach (var sibling in siblings)
        {
            sibling.Proportion = 0.4 * sibling.Proportion / remaining;
        }
        pane.Proportion = 0.6;
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        return context;
    }

    private static Point GraphPointer(MainWindowTestContext context, SubtitleTimelineControl timeline)
    {
        return timeline.TranslatePoint(new(timeline.HeaderWidth + timeline.Viewport.Width / 2,
            timeline.RulerHeight + timeline.Viewport.Height * 0.85), context.Window)!.Value;
    }

    private static void ZoomOutToWholeMedia(MainWindowTestContext context, SubtitleTimelineControl timeline, Point pointer,
        int durationSeconds = MEDIA_SECONDS)
    {
        var events = 0;
        while (timeline.VisibleDuration < durationSeconds)
        {
            Assert.True(events++ < 40);
            context.Window.MouseWheel(pointer, new(0, -2), RawInputModifiers.Control);
        }
        Assert.Equal(0, timeline.ViewStart);
    }

    private static void AssertToneEnergy(SpectrogramData spectrum, int durationSeconds = MEDIA_SECONDS)
    {
        var row = (int)(Math.Log(UiRapidZoomAudioSource.TONE_HERTZ / 40.0) / Math.Log(8000.0 / 40) * spectrum.Height);
        var tested = 0;
        for (var column = 0; column < spectrum.Width; column++)
        {
            var center = spectrum.Start + spectrum.ColumnDuration * column + spectrum.ColumnDuration / 2;
            if (center >= MediaTime.Zero && center < new MediaTime(durationSeconds))
            {
                Assert.True(spectrum.Levels.Span[row * spectrum.Width + column] > 150);
                tested++;
            }
        }
        Assert.True(tested > 0);
    }

    private static async Task AdvancePlaybackAsync(MainWindowTestContext context, UiAuditionAudioOutput output)
    {
        var started = Stopwatch.GetTimestamp();
        while (output.QueuedFrames < 4800)
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10));
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        var previous = context.Session.ProjectPosition;
        output.Consume(4800);
        started = Stopwatch.GetTimestamp();
        while (context.Session.ProjectPosition <= previous)
        {
            Assert.True(Stopwatch.GetElapsedTime(started) < TimeSpan.FromSeconds(10));
            context.Clock.Advance(TimeSpan.FromMilliseconds(5));
            context.Session.Tick();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
