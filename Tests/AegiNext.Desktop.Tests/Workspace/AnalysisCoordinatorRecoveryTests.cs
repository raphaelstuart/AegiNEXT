using AegiNext.Core.Timing;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AnalysisCoordinatorRecoveryTests
{
    [Fact]
    public async Task FailedBuildDoesNotPermanentlyDeduplicateTheSameViewport()
    {
        await using var context = CreateContext();
        await context.Session.Controller.OpenAsync("failed-analysis-window.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        var attempts = 0;
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = _ =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    throw new IOException("Injected analysis read failure.");
                }
            }
        };
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory) =>
            new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("failed-analysis-window.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(timeline.Waveform);
            Assert.Contains("Injected analysis read failure", timeline.AnalysisStatus);
            Assert.Equal(1, attempts);
            Assert.Single(context.Session.ApplicationContext.Tasks.GetSnapshots(), snapshot =>
                snapshot.Name == "Tasks.AudioAnalysis" && snapshot.State == AegiTaskState.Failed);
            timeline.Viewport = timeline.Viewport with { VerticalOffset = 25 };
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
            Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        }
        finally
        {
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task CompletedCacheReadFailureAutomaticallyRebuildsAndPublishesWithoutChangingTheViewport()
    {
        await using var context = CreateContext();
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("corrupt-analysis-cache.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        var originalViewport = timeline.Viewport;
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        AudioAnalysisSession? firstSession = null;
        string cacheDirectory;
        using (var first = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory) =>
                   firstSession = new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path)))
        {
            await first.StartAsync("corrupt-analysis-cache.mkv");
            await first.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            cacheDirectory = firstSession!.CacheDirectory;
            await first.ClearAsync();
        }
        var payloadPath = Path.Combine(cacheDirectory, "data.bin");
        var bytes = await File.ReadAllBytesAsync(payloadPath);
        for (var index = 0; index < bytes.Length; index++)
        {
            bytes[index] ^= 0xFF;
        }
        await File.WriteAllBytesAsync(payloadPath, bytes);
        var replacement = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        AudioAnalysisSession? current = null;
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory) =>
            current = new(_ => replacement, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            var start = context.Session.ApplicationContext.Tasks.Submit(
                new AnalysisStartTask(context.Session.TaskScope, coordinator, "corrupt-analysis-cache.mkv"));
            await start.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(originalViewport, timeline.Viewport);
            Assert.True(current!.IsCacheComplete);
            Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, replacement.FramesRead);
            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
            Assert.Equal(2, context.Session.ApplicationContext.Tasks.GetSnapshots().Count(snapshot =>
                snapshot.Name == "Tasks.AudioAnalysis" && snapshot.State == AegiTaskState.Succeeded));
        }
        finally
        {
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task ReplacingBlockedFineWaveformPublishesCachedSpectrumAndRejectsLateDetail()
    {
        await using var context = CreateContext();
        await context.Session.Controller.OpenAsync("cancelled-analysis-window.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detail = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE)
        {
            MaximumFrames = 24L * WaveformAnalyzer.SAMPLE_RATE,
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory) =>
            new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path,
                detailSourceFactory: _ => detail));
        try
        {
            await coordinator.StartAsync("cancelled-analysis-window.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var previous = timeline.Waveform;
            timeline.Viewport = timeline.Viewport with { StartSeconds = 2, PixelsPerSecond = 400 };
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(previous, timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.InRange((double)timeline.Spectrogram.ColumnDuration.Numerator / timeline.Spectrogram.ColumnDuration.Denominator, 0, 0.05);
            timeline.Viewport = timeline.Viewport with { StartSeconds = 4, PixelsPerSecond = 40 };
            release.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotNull(timeline.Waveform);
            Assert.True(timeline.Waveform.SamplesPerBucket >= 512);
            Assert.True(timeline.Waveform.Start <= new MediaTime(4));
            Assert.True(timeline.Waveform.End >= new MediaTime(8));
            Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
            Assert.Equal(0, source.CancelCount);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task CompletedOldDebounceCannotDisplaceTheLatestViewportWhenItsCallbackRunsLate()
    {
        await using var context = CreateContext();
        await context.Session.Controller.OpenAsync("late-analysis-continuation.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        var deferred = new DeferredAnalysisSynchronizationContext();
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory) =>
            new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("late-analysis-continuation.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var reads = source.FramesRead;
            deferred.Capture(() => timeline.Viewport = timeline.Viewport with { StartSeconds = 2 });
            await deferred.Posted.WaitAsync(TimeSpan.FromSeconds(5));
            timeline.Viewport = timeline.Viewport with { StartSeconds = 4, PixelsPerSecond = 20 };
            deferred.RunCallbacks();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            Assert.True(timeline.Waveform.Start <= new MediaTime(4));
            Assert.True(timeline.Waveform.End >= new MediaTime(8));
            Assert.Equal(string.Empty, timeline.AnalysisStatus);
            Assert.Equal(reads, source.FramesRead);
            var waveform = timeline.Waveform;
            timeline.Viewport = timeline.Viewport with { VerticalOffset = 25 };
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(waveform, timeline.Waveform);
        }
        finally
        {
            deferred.RunCallbacks();
            await coordinator.ClearAsync();
        }
    }

    private static WorkspaceSessionTestContext CreateContext()
    {
        return new(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(8), 1)),
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
