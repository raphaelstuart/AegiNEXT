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

/// <summary>验证播放中的真实缩放先发布频谱，再发布较慢波形，并复用完整视口的数据。</summary>
public sealed class TimelineAnalysisLayerPublicationUiTests
{
    private const int MEDIA_SECONDS = 300;

    /// <summary>波形解码仍在等待时全片频谱可用，取消旧波形请求不会覆盖下一次缩放的频谱。</summary>
    [AvaloniaFact]
    public async Task PlayingZoomPublishesSpectrumBeforeBlockedWaveformAndKeepsTheLatestRevision()
    {
        const int DURATION_SECONDS = 7200;
        const int SOURCE_BLOCK_FRAMES = 4096;
        var playback = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        var video = new PreviewTestSource(1, 0, DURATION_SECONDS * 1000L);
        await using var context = CreateContext(playback, output, video, DURATION_SECONDS);
        await context.Controller.OpenAsync("layer-publication.media", TestContext.Current.CancellationToken);
        context.Session.Tick();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var model = context.ViewModel.Timeline;
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = timeline.Viewport.Width / 8 };
        var source = new UiRapidZoomAudioSource(DURATION_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE);
        using var release = new ManualResetEventSlim();
        using var coordinator = new AnalysisCoordinator(context.Session,
            (_, _, mapping, duration) => new(_ => source, mapping, duration, 8L * 1024 * 1024));
        var firstSpectrum = new TaskCompletionSource<SpectrogramData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var latestSpectrum = new TaskCompletionSource<SpectrogramData>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enteredWaveformRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockWaveform = 0;
        var blocked = 0;
        SpectrogramData? previousFullSpectrum = null;
        void OnModelChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(TimelinePanelViewModel.Spectrogram) || model.Spectrogram is not { } spectrum ||
                spectrum.Start > MediaTime.Zero || spectrum.End < new MediaTime(DURATION_SECONDS))
            {
                return;
            }

            if (!firstSpectrum.Task.IsCompleted)
            {
                Interlocked.Exchange(ref blockWaveform, 1);
                firstSpectrum.TrySetResult(spectrum);
            }
            else if (previousFullSpectrum is { } previous && !ReferenceEquals(previous, spectrum))
            {
                latestSpectrum.TrySetResult(spectrum);
            }
        }

        try
        {
            await coordinator.StartAsync("synthetic-layer-publication.media");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.True(model.IsPlaybackFollowEnabled);
            var pointer = GraphPointer(context, timeline);
            context.Window.MouseWheel(pointer, new(0, 2), RawInputModifiers.Control);
            Assert.False(model.IsPlaybackFollowEnabled);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var narrowWaveform = Assert.IsType<WaveformData>(model.Waveform);
            var document = context.Session.DocumentSnapshot;
            var playbackSeeks = playback.SeekCount;
            var videoSeeks = video.SeekCount;
            var warmFrames = source.FramesRead;
            model.PropertyChanged += OnModelChanged;
            source.BeforeRead = token =>
            {
                if (Volatile.Read(ref blockWaveform) != 0 && Interlocked.CompareExchange(ref blocked, 1, 0) == 0)
                {
                    enteredWaveformRead.TrySetResult();
                    release.Wait(token);
                }
            };

            ZoomOutToWholeMedia(context, timeline, pointer, DURATION_SECONDS);
            var firstPlan = Assert.IsType<WaveformViewportPlan>(
                WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(DURATION_SECONDS)));
            Assert.Equal(AudioAnalysisMode.PREVIEW, firstPlan.Analysis.Mode);
            previousFullSpectrum = await firstSpectrum.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Same(narrowWaveform, model.Waveform);
            AssertToneEnergy(previousFullSpectrum, DURATION_SECONDS);
            await enteredWaveformRead.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(coordinator.Completion.IsCompleted);
            var position = context.Session.ProjectPosition;
            await AdvancePlaybackAsync(context, output);
            Assert.True(context.Session.ProjectPosition > position);
            Assert.Same(previousFullSpectrum, model.Spectrogram);
            Assert.Same(narrowWaveform, model.Waveform);

            context.Window.MouseWheel(pointer, new(0, -6), RawInputModifiers.Control);
            var finalPlan = Assert.IsType<WaveformViewportPlan>(
                WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(DURATION_SECONDS)));
            Assert.Equal(AudioAnalysisMode.PREVIEW, finalPlan.Analysis.Mode);
            Assert.Equal(MediaTime.Zero, finalPlan.Visible.Start);
            Assert.True(finalPlan.Visible.End >= new MediaTime(DURATION_SECONDS));
            release.Set();
            var newestSpectrum = await latestSpectrum.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.True(newestSpectrum.ColumnDuration > previousFullSpectrum.ColumnDuration);
            AssertToneEnergy(newestSpectrum, DURATION_SECONDS);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Same(newestSpectrum, model.Spectrogram);
            var waveform = Assert.IsType<WaveformData>(model.Waveform);
            Assert.Equal(finalPlan.Analysis, waveform.Request);
            Assert.Contains(waveform.Peaks.ToArray(), value => value > 0.7F);
            Assert.Contains(waveform.Peaks.ToArray(), value => value < -0.7F);
            Assert.Equal(string.Empty, model.AnalysisStatus);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.False(model.IsPlaybackFollowEnabled);
            Assert.Equal(playbackSeeks, playback.SeekCount);
            Assert.Equal(videoSeeks, video.SeekCount);
            Assert.Same(document, context.Session.DocumentSnapshot);
            var tileFrames = AudioAnalysisSampleReader.PREVIEW_TILE_SAMPLES + AudioAnalysisSampleReader.PADDING * 2;
            var maximumFramesPerWindow = (tileFrames + SOURCE_BLOCK_FRAMES - 1) / SOURCE_BLOCK_FRAMES * SOURCE_BLOCK_FRAMES;
            var sparseFrameBudget = (long)(previousFullSpectrum.Width + newestSpectrum.Width + finalPlan.Analysis.BucketCount + 1) *
                                    maximumFramesPerWindow;
            Assert.True(sparseFrameBudget < DURATION_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE / 5,
                "两个预览 revision 的窗口预算仍须明显低于全片解码量。");
            Assert.InRange(source.FramesRead - warmFrames, 1, sparseFrameBudget);
            Assert.Equal(0, source.CancelCount);
        }
        finally
        {
            release.Set();
            model.PropertyChanged -= OnModelChanged;
            await coordinator.ClearAsync();
        }
        Assert.Equal(1, source.DisposeCount);
    }

    /// <summary>播放中的全片视口重复缩小后继续发布能量，复用已缓存 PCM 且不 seek 播放源。</summary>
    [AvaloniaFact]
    public async Task RepeatedWholeMediaZoomWhilePlayingReusesPcmAndDoesNotSeekPlayback()
    {
        var playback = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        var video = new PreviewTestSource(1, 0, MEDIA_SECONDS * 1000L);
        await using var context = CreateContext(playback, output, video);
        await context.Controller.OpenAsync("cached-layer-publication.media", TestContext.Current.CancellationToken);
        context.Session.Tick();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var model = context.ViewModel.Timeline;
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = timeline.Viewport.Width / 8 };
        var source = new UiRapidZoomAudioSource(MEDIA_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session,
            (_, _, mapping, duration) => new(_ => source, mapping, duration, 64L * 1024 * 1024));
        try
        {
            await coordinator.StartAsync("synthetic-cached-layer-publication.media");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            Assert.True(model.IsPlaybackFollowEnabled);
            var pointer = GraphPointer(context, timeline);
            context.Window.MouseWheel(pointer, new(0, 2), RawInputModifiers.Control);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            ZoomOutToWholeMedia(context, timeline, pointer);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
            var warmFrames = source.FramesRead;
            var analysisSeeks = source.SeekCount;
            var playbackSeeks = playback.SeekCount;
            var videoSeeks = video.SeekCount;
            var document = context.Session.DocumentSnapshot;
            for (var iteration = 0; iteration < 6; iteration++)
            {
                context.Window.MouseWheel(pointer, new(0, -2), RawInputModifiers.Control);
                await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
                AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram));
                var plan = Assert.IsType<WaveformViewportPlan>(
                    WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(MEDIA_SECONDS)));
                Assert.Equal(plan.Analysis, Assert.IsType<WaveformData>(model.Waveform).Request);
                await AdvancePlaybackAsync(context, output);
                Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
                Assert.False(model.IsPlaybackFollowEnabled);
            }
            Assert.Equal(warmFrames, source.FramesRead);
            Assert.Equal(analysisSeeks, source.SeekCount);
            Assert.Equal(0, source.CancelCount);
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

    /// <summary>真实缩放从冷缓存进入粗预览，局部精确分析后恢复同一预览不再读取媒体。</summary>
    [AvaloniaFact]
    public async Task PlayingColdPreviewPublishesSpectrumFirstAndRestoresItsCacheAfterLocalExactZoom()
    {
        const int DURATION_SECONDS = 7200;
        const int SOURCE_BLOCK_FRAMES = 4096;
        var playback = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        var video = new PreviewTestSource(1, 0, DURATION_SECONDS * 1000L);
        await using var context = CreateContext(playback, output, video, DURATION_SECONDS);
        await context.Controller.OpenAsync("cold-preview-layer-publication.media", TestContext.Current.CancellationToken);
        context.Session.Tick();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var model = context.ViewModel.Timeline;
        model.Viewport = timeline.Viewport with { StartSeconds = 0, PixelsPerSecond = timeline.Viewport.Width / 8 };
        var source = new UiRapidZoomAudioSource(DURATION_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session,
            (_, _, mapping, duration) => new(_ => source, mapping, duration, 8L * 1024 * 1024));
        var publishedSpectrum = new TaskCompletionSource<(SpectrogramData Spectrum, WaveformData? Waveform)>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var publications = new List<string>();
        WaveformViewportPlan? previewPlan = null;
        void OnModelChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (previewPlan is not { } expected)
            {
                return;
            }
            if (args.PropertyName == nameof(TimelinePanelViewModel.Spectrogram) && model.Spectrogram is { } spectrum &&
                spectrum.Start <= expected.Visible.Start && spectrum.End >= expected.Visible.End)
            {
                publications.Add("Spectrum");
                publishedSpectrum.TrySetResult((spectrum, model.Waveform));
            }
            else if (args.PropertyName == nameof(TimelinePanelViewModel.Waveform) && model.Waveform is { } waveform &&
                     waveform.Request == expected.Analysis)
            {
                publications.Add("Waveform");
            }
        }

        try
        {
            await coordinator.StartAsync("synthetic-cold-preview-layer-publication.media");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await context.Session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.True(model.IsPlaybackFollowEnabled);
            var pointer = GraphPointer(context, timeline);
            context.Window.MouseWheel(pointer, new(0, 2), RawInputModifiers.Control);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            var narrowWaveform = Assert.IsType<WaveformData>(model.Waveform);
            Assert.Equal(AudioAnalysisMode.EXACT, narrowWaveform.Request.Mode);
            var document = context.Session.DocumentSnapshot;
            var playbackSeeks = playback.SeekCount;
            var videoSeeks = video.SeekCount;
            var warmFrames = source.FramesRead;
            var warmSeeks = source.SeekCount;
            model.PropertyChanged += OnModelChanged;
            var zoomEvents = 0;
            do
            {
                Assert.True(zoomEvents++ < 50);
                context.Window.MouseWheel(pointer, new(0, -2), RawInputModifiers.Control);
                previewPlan = Assert.IsType<WaveformViewportPlan>(
                    WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(DURATION_SECONDS)));
            }
            while (previewPlan.Analysis.Mode != AudioAnalysisMode.PREVIEW || timeline.VisibleDuration < DURATION_SECONDS);
            Assert.Equal(MediaTime.Zero, previewPlan.Visible.Start);
            Assert.True(previewPlan.Visible.End >= new MediaTime(DURATION_SECONDS));
            var firstPublication = await publishedSpectrum.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            Assert.Same(narrowWaveform, firstPublication.Waveform);
            AssertToneEnergy(firstPublication.Spectrum, DURATION_SECONDS);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            model.PropertyChanged -= OnModelChanged;
            Assert.Collection(publications, value => Assert.Equal("Spectrum", value), value => Assert.Equal("Waveform", value));
            var preview = Assert.IsType<WaveformData>(model.Waveform);
            var previewSpectrum = Assert.IsType<SpectrogramData>(model.Spectrogram);
            Assert.Equal(previewPlan.Analysis, preview.Request);
            Assert.Equal(AudioAnalysisMode.PREVIEW, preview.Request.Mode);
            Assert.True(preview.Start <= previewPlan.Visible.Start && preview.End >= previewPlan.Visible.End);
            Assert.True(previewSpectrum.Start <= previewPlan.Visible.Start && previewSpectrum.End >= previewPlan.Visible.End);
            Assert.Contains(preview.Peaks.ToArray(), value => value > 0.7F);
            Assert.Contains(preview.Peaks.ToArray(), value => value < -0.7F);
            var tileFrames = AudioAnalysisSampleReader.PREVIEW_TILE_SAMPLES + AudioAnalysisSampleReader.PADDING * 2;
            var maximumFramesPerWindow = (tileFrames + SOURCE_BLOCK_FRAMES - 1) / SOURCE_BLOCK_FRAMES * SOURCE_BLOCK_FRAMES;
            var sparseFrameBudget = (long)(previewPlan.Analysis.BucketCount + previewSpectrum.Width) * maximumFramesPerWindow;
            var mediaSamples = DURATION_SECONDS * (long)WaveformAnalyzer.SAMPLE_RATE;
            Assert.True(sparseFrameBudget < mediaSamples / 5, "冷预览窗口预算必须明显低于全片解码量。");
            Assert.InRange(source.FramesRead - warmFrames, 1, sparseFrameBudget);
            Assert.InRange(source.SeekCount - warmSeeks, 1, previewPlan.Analysis.BucketCount + previewSpectrum.Width + 2);
            var previewPeaks = preview.Peaks.ToArray();
            var previewLevels = previewSpectrum.Levels.ToArray();
            await AdvancePlaybackAsync(context, output);

            var localFrames = source.FramesRead;
            var localSeeks = source.SeekCount;
            var zoomInEvents = 0;
            WaveformViewportPlan localPlan;
            do
            {
                Assert.True(zoomInEvents++ < 50);
                context.Window.MouseWheel(pointer, new(0, 2), RawInputModifiers.Control);
                localPlan = Assert.IsType<WaveformViewportPlan>(
                    WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(DURATION_SECONDS)));
            }
            while (localPlan.Analysis.Mode != AudioAnalysisMode.EXACT || timeline.VisibleDuration > 8);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            var exact = Assert.IsType<WaveformData>(model.Waveform);
            Assert.Equal(localPlan.Analysis, exact.Request);
            Assert.Equal(AudioAnalysisMode.EXACT, exact.Request.Mode);
            Assert.True(exact.Start <= localPlan.Visible.Start && exact.End >= localPlan.Visible.End);
            AssertToneEnergy(Assert.IsType<SpectrogramData>(model.Spectrogram), DURATION_SECONDS);
            var localSamples = localPlan.Analysis.Duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
            Assert.InRange(source.FramesRead - localFrames, 1, localSamples * 2 + 8L * WaveformAnalyzer.SAMPLE_RATE);
            Assert.InRange(source.SeekCount - localSeeks, 1, 4);
            var restoreFrames = source.FramesRead;
            var restoreSeeks = source.SeekCount;
            for (var index = 0; index < zoomInEvents; index++)
            {
                context.Window.MouseWheel(pointer, new(0, -2), RawInputModifiers.Control);
            }
            var restoredPlan = Assert.IsType<WaveformViewportPlan>(
                WaveformViewportPlanner.Create(timeline.Viewport, model.RenderScaling, new(DURATION_SECONDS)));
            Assert.Equal(previewPlan, restoredPlan);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            var restored = Assert.IsType<WaveformData>(model.Waveform);
            var restoredSpectrum = Assert.IsType<SpectrogramData>(model.Spectrogram);
            Assert.Equal(previewPlan.Analysis, restored.Request);
            Assert.Equal(previewPeaks, restored.Peaks.ToArray());
            Assert.Equal(previewLevels, restoredSpectrum.Levels.ToArray());
            Assert.Equal(restoreFrames, source.FramesRead);
            Assert.Equal(restoreSeeks, source.SeekCount);
            Assert.Equal(0, source.CancelCount);
            Assert.Equal(playbackSeeks, playback.SeekCount);
            Assert.Equal(videoSeeks, video.SeekCount);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
            Assert.False(model.IsPlaybackFollowEnabled);
            Assert.Equal(string.Empty, model.AnalysisStatus);
        }
        finally
        {
            model.PropertyChanged -= OnModelChanged;
            await coordinator.ClearAsync();
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
