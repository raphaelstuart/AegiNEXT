using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AnalysisCoordinatorTests
{
    [Fact]
    public async Task HidingOnlyWaveformInvalidatesTheSameViewportAndReusesBothCachedLayers()
    {
        await using var context = CreateContext(21600);
        await context.Session.Controller.OpenAsync("layer-switch-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        var source = new AnalysisBudgetSource(21600L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ => source, mapping, duration));
        await coordinator.StartAsync("layer-switch-analysis.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        var reads = source.FramesRead;

        timeline.IsWaveformVisible = false;
        Assert.Null(timeline.Waveform);
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        Assert.Equal(reads, source.FramesRead);
        timeline.IsWaveformVisible = true;
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        Assert.Equal(reads, source.FramesRead);
        Assert.Equal(0, source.CancelCount);
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task HidingWaveformWhileItsWindowIsBlockedPreventsItsLatePublicationAndKeepsSpectrumUsable()
    {
        await using var context = CreateContext(21600);
        await context.Session.Controller.OpenAsync("inflight-layer-switch-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new AnalysisBudgetSource(21600L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ => source, mapping, duration));
        try
        {
            await coordinator.StartAsync("inflight-layer-switch-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            timeline.IsWaveformVisible = false;
            release.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.Equal(0, source.CancelCount);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task ReplacingOrClosingMediaCancelsTheOldReadAndDrainsItsDecoderBeforeUsingTheNextOne()
    {
        await using var context = CreateContext(21600);
        await context.Session.Controller.OpenAsync("replace-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSource = new AnalysisBudgetSource(21600L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        var newSource = new AnalysisBudgetSource(21600L * WaveformAnalyzer.SAMPLE_RATE);
        var opened = 0;
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) =>
            new(_ => Interlocked.Increment(ref opened) == 1 ? oldSource : newSource, mapping, duration));
        try
        {
            await coordinator.StartAsync("old-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.StartAsync("new-analysis.mkv").WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, oldSource.CancelCount);
            Assert.Equal(1, oldSource.DisposeCount);
            Assert.Equal(2, opened);
            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            await coordinator.ClearAsync();
            Assert.Equal(1, newSource.CancelCount);
            Assert.Equal(1, newSource.DisposeCount);
            Assert.Null(timeline.Waveform);
            Assert.Null(timeline.Spectrogram);
            Assert.Equal(MediaTime.Zero, timeline.AudioDuration);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
    }

    [Theory]
    [InlineData(30)]
    [InlineData(7800)]
    [InlineData(21600)]
    public async Task StartingAnalysisOnlyReadsVisibleMediaAndNeverStartsAFullFileOverview(int seconds)
    {
        await using var context = CreateContext(seconds);
        await context.Session.Controller.OpenAsync("local-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        var source = new AnalysisBudgetSource((long)seconds * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ => source, mapping, duration));

        await coordinator.StartAsync("local-analysis.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        Assert.Null(timeline.WaveformOverview);
        Assert.Null(timeline.SpectrogramOverview);
        Assert.Equal(string.Empty, timeline.AnalysisStatus);
        Assert.InRange(source.FramesRead, 1, 6L * WaveformAnalyzer.SAMPLE_RATE);
        await coordinator.ClearAsync();
        Assert.Equal(1, source.CancelCount);
        Assert.Equal(1, source.DisposeCount);
    }

    [Fact]
    public async Task StartingWithBothLayersHiddenDoesNotOpenOrReadADecoder()
    {
        await using var context = CreateContext(21600);
        await context.Session.Controller.OpenAsync("hidden-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        timeline.IsWaveformVisible = false;
        timeline.IsSpectrumVisible = false;
        var opened = 0;
        var source = new AnalysisBudgetSource(21600L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ =>
        {
            Interlocked.Increment(ref opened);
            return source;
        }, mapping, duration));

        await coordinator.StartAsync("hidden-analysis.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, opened);
        Assert.Equal(0, source.FramesRead);
        Assert.Null(timeline.Waveform);
        Assert.Null(timeline.Spectrogram);
        Assert.Equal(string.Empty, timeline.AnalysisStatus);
        await coordinator.ClearAsync();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HiddenLayersAreNotPublishedAndRepeatedViewportReusesDecodedData(bool waveform, bool spectrum)
    {
        await using var context = CreateContext(21600);
        await context.Session.Controller.OpenAsync("one-analysis-layer.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        timeline.IsWaveformVisible = waveform;
        timeline.IsSpectrumVisible = spectrum;
        var source = new AnalysisBudgetSource(21600L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ => source, mapping, duration));
        await coordinator.StartAsync("one-analysis-layer.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(waveform, timeline.Waveform is not null);
        Assert.Equal(spectrum, timeline.Spectrogram is not null);
        var reads = source.FramesRead;
        timeline.Viewport = timeline.Viewport with { VerticalOffset = 25 };
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(reads, source.FramesRead);
        Assert.Equal(0, source.CancelCount);
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task HidingBothLayersSupersedesABlockedWindowWithoutPermanentlyCancellingItsDecoder()
    {
        await using var context = CreateContext(21600);
        await context.Session.Controller.OpenAsync("hide-active-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new AnalysisBudgetSource(21600L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ => source, mapping, duration));
        try
        {
            await coordinator.StartAsync("hide-active-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            timeline.IsWaveformVisible = false;
            timeline.IsSpectrumVisible = false;
            release.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, source.CancelCount);
            Assert.InRange(source.FramesRead, 0, 4096);
            Assert.Null(timeline.Waveform);
            Assert.Null(timeline.Spectrogram);
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
    }

    private static WorkspaceSessionTestContext CreateContext(int seconds)
    {
        return new(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(seconds), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 100), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, target, _) => Task.FromResult(new AudioPlaybackSession(new CalibrationAudioSource(), new CalibrationAudioOutput("analysis-test"), target))));
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
