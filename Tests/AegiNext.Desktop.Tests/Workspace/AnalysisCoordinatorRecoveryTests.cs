using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AnalysisCoordinatorRecoveryTests
{
    /// <summary>分析失败后，同一时间范围的刷新可以重新提交并完成两层数据。</summary>
    [Fact]
    public async Task FailedWindowDoesNotPermanentlyDeduplicateTheSameViewport()
    {
        await using var context = CreateContext();
        await context.Session.Controller.OpenAsync("failed-analysis-window.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        var attempts = 0;
        var source = new AnalysisBudgetSource(10800L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new IOException("Injected analysis read failure.");
                }
            }
        };
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ => source, mapping, duration));
        try
        {
            await coordinator.StartAsync("failed-analysis-window.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(timeline.Waveform);
            Assert.Contains("Injected analysis read failure", timeline.AnalysisStatus);

            timeline.Viewport = timeline.Viewport with { VerticalOffset = 25 };
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
            Assert.InRange(source.FramesRead, 1, 6L * WaveformAnalyzer.SAMPLE_RATE);
            Assert.Equal(0, source.CancelCount);
        }
        finally
        {
            await coordinator.ClearAsync();
        }
    }

    /// <summary>当前请求被替换取消后，同一视口不会因旧计划记录而永远失去分析结果。</summary>
    [Fact]
    public async Task CancelledWindowDoesNotPermanentlyDeduplicateTheSameViewport()
    {
        await using var context = CreateContext();
        await context.Session.Controller.OpenAsync("cancelled-analysis-window.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new AnalysisBudgetSource(10800L * WaveformAnalyzer.SAMPLE_RATE)
        {
            MaximumFrames = 12L * WaveformAnalyzer.SAMPLE_RATE,
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        AudioAnalysisSession? current = null;
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) =>
            current = new(_ => source, mapping, duration));
        try
        {
            await coordinator.StartAsync("cancelled-analysis-window.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var replacement = current!.GetLayersAsync(new(new(20), 512, 128), true, true);
            release.Set();
            await replacement.WaitAsync(TimeSpan.FromSeconds(10));
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(timeline.Waveform);
            Assert.Null(timeline.Spectrogram);

            timeline.Viewport = timeline.Viewport with { VerticalOffset = 25 };
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.True(timeline.Waveform.Start <= new MediaTime(10));
            Assert.True(timeline.Waveform.End >= new MediaTime(12));
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
            Assert.Equal(0, source.CancelCount);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
    }

    /// <summary>任务启动回调延迟派发时，批次只读取最新视口并完整发布。</summary>
    [Fact]
    public async Task CompletedOldDebounceCannotDisplaceTheLatestViewportWhenItsCallbackRunsLate()
    {
        await using var context = CreateContext();
        await context.Session.Controller.OpenAsync("late-analysis-continuation.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(10, 400, Width: 800);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var blockNextRead = 0;
        var source = new AnalysisBudgetSource(10800L * WaveformAnalyzer.SAMPLE_RATE)
        {
            MaximumFrames = 100L * WaveformAnalyzer.SAMPLE_RATE,
            BeforeRead = token =>
            {
                if (Interlocked.Exchange(ref blockNextRead, 0) == 1)
                {
                    entered.TrySetResult();
                    release.Wait(token);
                }
            }
        };
        var deferred = new DeferredAnalysisSynchronizationContext();
        var captured = false;
        using var coordinator = new AnalysisCoordinator(context.Session, (_, _, mapping, duration) => new(_ => source, mapping, duration));
        try
        {
            await coordinator.StartAsync("late-analysis-continuation.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var previous = timeline.Waveform;
            captured = true;
            deferred.Capture(() => timeline.Viewport = timeline.Viewport with { StartSeconds = 20 });
            await deferred.Posted.WaitAsync(TimeSpan.FromSeconds(10));

            Interlocked.Exchange(ref blockNextRead, 1);
            timeline.Viewport = timeline.Viewport with { StartSeconds = 300, PixelsPerSecond = 20 };
            deferred.RunCallbacks();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(previous, timeline.Waveform);
            Assert.False(coordinator.Completion.IsCompleted);
            release.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.True(timeline.Waveform.Start <= new MediaTime(300));
            Assert.True(timeline.Waveform.End >= new MediaTime(340),
                $"Waveform [{timeline.Waveform.Start}, {timeline.Waveform.End}); viewport {timeline.Viewport}; status {timeline.AnalysisStatus}");
            Assert.True(timeline.Spectrogram.Start <= new MediaTime(300));
            Assert.True(timeline.Spectrogram.End >= new MediaTime(340));
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
            Assert.Equal(0, source.CancelCount);
            var waveform = timeline.Waveform;
            var spectrum = timeline.Spectrogram;
            var reads = source.FramesRead;
            timeline.Viewport = timeline.Viewport with { VerticalOffset = 25 };
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(waveform, timeline.Waveform);
            Assert.Same(spectrum, timeline.Spectrogram);
            Assert.Equal(reads, source.FramesRead);
        }
        finally
        {
            release.Set();
            coordinator.Cancel();
            if (captured)
            {
                await deferred.Posted.WaitAsync(TimeSpan.FromSeconds(10));
            }
            deferred.RunCallbacks();
            await coordinator.ClearAsync();
        }
    }

    private static WorkspaceSessionTestContext CreateContext()
    {
        return new(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(10800), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 100), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, target, _) => Task.FromResult(new AudioPlaybackSession(new CalibrationAudioSource(), new CalibrationAudioOutput("analysis-recovery-test"), target))));
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
