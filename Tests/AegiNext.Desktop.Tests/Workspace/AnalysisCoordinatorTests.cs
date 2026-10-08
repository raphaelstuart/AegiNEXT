using AegiNext.Core.Timing;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AnalysisCoordinatorTests
{
    [Fact]
    public async Task HidingOnlyWaveformReusesThePersistentLayersWithoutFurtherMediaReads()
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("layer-switch-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = CreateCoordinator(context, source);
        await coordinator.StartAsync("layer-switch-analysis.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.NotNull(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        var reads = source.FramesRead;
        var seeks = source.SeekCount;

        timeline.IsWaveformVisible = false;
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        timeline.IsWaveformVisible = true;
        timeline.Viewport = timeline.Viewport with { PixelsPerSecond = 40 };
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        Assert.Equal(reads, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task HidingBothLayersWhileTheBuilderIsBlockedDoesNotCancelTheBuild()
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("hide-active-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        using var coordinator = CreateCoordinator(context, source);
        try
        {
            await coordinator.StartAsync("hide-active-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            timeline.IsWaveformVisible = false;
            timeline.IsSpectrumVisible = false;
            release.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
            Assert.Equal(0, source.CancelCount);
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

    [Fact]
    public async Task ReplacingMediaCancelsTheOldBuildAndDrainsItsDecoderBeforeOpeningTheNextOne()
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("replace-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSource = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        var newSource = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory, options, budget) =>
            new(_ => path == "old-analysis.mkv" ? oldSource : newSource, mapping, duration,
                cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("old-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.StartAsync("new-analysis.mkv").WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, oldSource.CancelCount);
            Assert.Equal(1, oldSource.DisposeCount);
            Assert.NotNull(timeline.Waveform);
            Assert.NotNull(timeline.Spectrogram);
            await coordinator.ClearAsync();
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
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(12)]
    public async Task StartingAnalysisBuildsTheWholeMediaOnceAndOrdinaryZoomReadsOnlyTheCache(int seconds)
    {
        await using var context = CreateContext(seconds);
        await context.Session.Controller.OpenAsync("full-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource((long)seconds * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = CreateCoordinator(context, source);

        await coordinator.StartAsync("full-analysis.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal((long)seconds * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        Assert.NotNull(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        Assert.True(Directory.EnumerateFiles(Path.Combine(context.Session.ProjectDirectory, "caches", "audio"), "*",
            SearchOption.AllDirectories).Any());
        var reads = source.FramesRead;
        var seeks = source.SeekCount;
        timeline.Viewport = timeline.Viewport with { PixelsPerSecond = 20, StartSeconds = 1 };
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(reads, source.FramesRead);
        Assert.Equal(seeks, source.SeekCount);
        Assert.Equal(string.Empty, timeline.AnalysisStatus);
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task StartingWithBothLayersHiddenStillBuildsBothLayersForLaterVisibility()
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("hidden-analysis.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        timeline.IsWaveformVisible = false;
        timeline.IsSpectrumVisible = false;
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = CreateCoordinator(context, source);
        await coordinator.StartAsync("hidden-analysis.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        Assert.Null(timeline.Waveform);
        Assert.Null(timeline.Spectrogram);
        var reads = source.FramesRead;
        timeline.IsWaveformVisible = true;
        timeline.IsSpectrumVisible = true;
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(timeline.Waveform);
        Assert.NotNull(timeline.Spectrogram);
        Assert.Equal(reads, source.FramesRead);
        await coordinator.ClearAsync();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task HiddenLayersAreNotPublishedAndRepeatedViewportReusesTheCache(bool waveform, bool spectrum)
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("one-analysis-layer.mkv");
        var timeline = context.Session.ViewModel.Timeline;
        timeline.Viewport = new(0, 80, Width: 640);
        timeline.IsWaveformVisible = waveform;
        timeline.IsSpectrumVisible = spectrum;
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = CreateCoordinator(context, source);
        await coordinator.StartAsync("one-analysis-layer.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(waveform, timeline.Waveform is not null);
        Assert.Equal(spectrum, timeline.Spectrogram is not null);
        var reads = source.FramesRead;
        timeline.Viewport = timeline.Viewport with { VerticalOffset = 25 };
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(reads, source.FramesRead);
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task ClearCancelsAQueuedBuilderWhileAnotherTaskOwnsTheOnlyExecutionSlot()
    {
        await using var context = CreateContext(8);
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("queued-analysis.mkv");
        context.Session.ApplicationContext.Tasks.MaximumConcurrentTasks = 1;
        var blocker = new WorkspaceLifecycleTask(context.Session.TaskScope);
        var handle = context.Session.ApplicationContext.Tasks.Submit(blocker);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        using var coordinator = CreateCoordinator(context, source);
        try
        {
            await blocker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
            await coordinator.StartAsync("queued-analysis.mkv");
            await coordinator.ClearAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, source.FramesRead);
            Assert.False(handle.Completion.IsCompleted);
        }
        finally
        {
            blocker.Finish.TrySetResult();
            blocker.Cleanup.TrySetResult();
            await handle.Completion;
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task ClearInsideAWorkflowCancelsAYieldedBuilderWithoutDeadlockingTheOnlyExecutionSlot()
    {
        await using var context = CreateContext(12);
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("yielded-analysis.mkv");
        context.Session.ApplicationContext.Tasks.MaximumConcurrentTasks = 1;
        context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new AnalysisBudgetSource(12L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        using var coordinator = CreateCoordinator(context, source);
        try
        {
            await coordinator.StartAsync("yielded-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var clear = context.Session.ApplicationContext.Tasks.Submit(new AnalysisClearTask(context.Session.TaskScope, coordinator));
            release.Set();
            await clear.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(context.Session.ViewModel.Timeline.Waveform);
            Assert.Null(context.Session.ViewModel.Timeline.Spectrogram);
            Assert.Equal(1, source.DisposeCount);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task RepeatingTheSameSaveWhileACopyWaitsForItsDestinationStillCompletesTheLatestMigration()
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("repeat-migrate-analysis.mkv");
        context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        AudioAnalysisSession? current = null;
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory, options, budget) =>
            current = new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("repeat-migrate-analysis.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var destination = Path.Combine(context.DirectoryPath, "repeat-target");
            var cacheDirectory = Path.Combine(destination, "caches", "audio");
            Directory.CreateDirectory(cacheDirectory);
            using (var destinationLease = new FileStream(Path.Combine(cacheDirectory, Path.GetFileName(current!.CacheDirectory)) + ".lock",
                       FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                coordinator.ProjectDirectoryChanged(destination);
                coordinator.ProjectDirectoryChanged(destination);
            }
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(cacheDirectory, Path.GetDirectoryName(current.CacheDirectory));
            Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        }
        finally
        {
            await coordinator.ClearAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingBeforeAnOldMigrationContinuationIsDispatchedUsesTheActualCacheLocation(bool returnToOriginal)
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("late-migrate-analysis.mkv");
        context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        AudioAnalysisSession? current = null;
        var deferred = new DeferredAnalysisSynchronizationContext();
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory, options, budget) =>
            current = new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("late-migrate-analysis.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var originalDirectory = context.Session.ProjectDirectory;
            var destination = Path.Combine(context.DirectoryPath, "deferred-target");
            var cacheDirectory = Path.Combine(destination, "caches", "audio");
            Directory.CreateDirectory(cacheDirectory);
            using (var destinationLease = new FileStream(Path.Combine(cacheDirectory, Path.GetFileName(current!.CacheDirectory)) + ".lock",
                       FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                deferred.Capture(() => coordinator.ProjectDirectoryChanged(destination));
                await deferred.Posted.WaitAsync(TimeSpan.FromSeconds(5));
                deferred.Capture(deferred.RunCallbacks);
            }
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (Path.GetDirectoryName(current.CacheDirectory) != cacheDirectory)
            {
                Assert.True(DateTime.UtcNow < deadline, "The media cache did not finish its physical relocation.");
                await Task.Delay(5);
            }
            Assert.False(coordinator.Completion.IsCompleted);
            var latest = returnToOriginal ? originalDirectory : destination;
            coordinator.ProjectDirectoryChanged(latest);
            deferred.Capture(deferred.RunCallbacks);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Path.Combine(latest, "caches", "audio"), Path.GetDirectoryName(current.CacheDirectory));
            Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
            var snapshots = context.Session.ApplicationContext.Tasks.GetSnapshots();
            Assert.Contains(snapshots, snapshot => snapshot.Name == "Tasks.AudioCacheMigration" && snapshot.State == AegiTaskState.Cancelled);
            Assert.Contains(snapshots, snapshot => snapshot.Name == "Tasks.AudioCacheMigration" && snapshot.State == AegiTaskState.Succeeded);
            var request = context.Session.ViewModel.Timeline.Waveform!.Request;
            var layers = await current.GetLayersAsync(request, true, true);
            Assert.Equal(context.Session.ViewModel.Timeline.Waveform.Peaks.ToArray(), layers.Waveform!.Peaks.ToArray());
            Assert.NotNull(layers.Spectrogram);
        }
        finally
        {
            deferred.Capture(deferred.RunCallbacks);
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task FailedCacheMigrationLeavesThePhysicalCacheUsableAndAnotherSaveRetriesFromIt()
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("failed-migrate-analysis.mkv");
        context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        AudioAnalysisSession? current = null;
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory, options, budget) =>
            current = new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("failed-migrate-analysis.mkv");
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var original = current!.CacheDirectory;
            var blocked = Path.Combine(context.DirectoryPath, "blocked-directory");
            await File.WriteAllTextAsync(blocked, "This file prevents a cache directory from being created.");
            coordinator.ProjectDirectoryChanged(blocked);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(original, current.CacheDirectory);
            Assert.Contains(context.Session.ApplicationContext.Tasks.GetSnapshots(), snapshot =>
                snapshot.Name == "Tasks.AudioCacheMigration" && snapshot.State == AegiTaskState.Failed);
            var reads = source.FramesRead;
            context.Session.ViewModel.Timeline.Viewport = context.Session.ViewModel.Timeline.Viewport with { PixelsPerSecond = 40 };
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.NotNull(context.Session.ViewModel.Timeline.Waveform);
            Assert.NotNull(context.Session.ViewModel.Timeline.Spectrogram);
            Assert.Equal(reads, source.FramesRead);
            var destination = Path.Combine(context.DirectoryPath, "successful-retry");
            coordinator.ProjectDirectoryChanged(destination);
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Path.Combine(destination, "caches", "audio"), Path.GetDirectoryName(current.CacheDirectory));
            Assert.Equal(reads, source.FramesRead);
            Assert.True(Directory.Exists(original));
        }
        finally
        {
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task ConsecutiveSavesCancelAQueuedCopyAndMigrateTheCompletedCacheToOnlyTheLatestDirectory()
    {
        await using var context = CreateContext(8);
        await context.InitializeAsync();
        await context.Session.Controller.OpenAsync("queued-migrate-analysis.mkv");
        context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE);
        AudioAnalysisSession? current = null;
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory, options, budget) =>
            current = new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        await coordinator.StartAsync("queued-migrate-analysis.mkv");
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        context.Session.ApplicationContext.Tasks.MaximumConcurrentTasks = 1;
        var blocker = new WorkspaceLifecycleTask(context.Session.TaskScope);
        var handle = context.Session.ApplicationContext.Tasks.Submit(blocker);
        try
        {
            await blocker.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var original = current!.CacheDirectory;
            var first = Path.Combine(context.DirectoryPath, "cancelled-copy");
            var latest = Path.Combine(context.DirectoryPath, "latest-copy");
            coordinator.ProjectDirectoryChanged(first);
            coordinator.ProjectDirectoryChanged(latest);
            Assert.Equal(original, current.CacheDirectory);
            Assert.Contains(context.Session.ApplicationContext.Tasks.GetSnapshots(), snapshot =>
                snapshot.Name == "Tasks.AudioCacheMigration" && snapshot.State == AegiTaskState.Cancelled);
            blocker.Finish.TrySetResult();
            blocker.Cleanup.TrySetResult();
            await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(Path.Combine(latest, "caches", "audio"), Path.GetDirectoryName(current.CacheDirectory));
            Assert.False(Directory.Exists(first));
            Assert.Equal(8L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
        }
        finally
        {
            blocker.Finish.TrySetResult();
            blocker.Cleanup.TrySetResult();
            await handle.Completion;
            await coordinator.ClearAsync();
        }
    }

    [Fact]
    public async Task SavingDuringConstructionMigratesFromTheOriginalCacheToTheLatestProjectDirectory()
    {
        await using var context = CreateContext(8);
        await context.Session.Controller.OpenAsync("migrate-analysis.mkv");
        context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new AnalysisBudgetSource(8L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        AudioAnalysisSession? current = null;
        using var coordinator = new AnalysisCoordinator(context.Session, (path, _, mapping, duration, directory, options, budget) =>
            current = new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
        try
        {
            await coordinator.StartAsync("migrate-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var original = current!.CacheDirectory;
            var first = Path.Combine(context.DirectoryPath, "first");
            var latest = Path.Combine(context.DirectoryPath, "latest");
            coordinator.ProjectDirectoryChanged(first);
            coordinator.ProjectDirectoryChanged(latest);
            Assert.Equal(original, current.CacheDirectory);
            Assert.False(Directory.Exists(Path.Combine(first, "caches", "audio")));
            release.Set();
            await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(Path.Combine(latest, "caches", "audio"), Path.GetDirectoryName(current.CacheDirectory));
            Assert.True(Directory.Exists(original));
            Assert.False(Directory.Exists(Path.Combine(first, "caches", "audio")));
            Assert.NotNull(context.Session.ViewModel.Timeline.Waveform);
            Assert.NotNull(context.Session.ViewModel.Timeline.Spectrogram);
        }
        finally
        {
            release.Set();
            await coordinator.ClearAsync();
        }
    }

    private static AnalysisCoordinator CreateCoordinator(WorkspaceSessionTestContext context, AnalysisBudgetSource source)
    {
        return new(context.Session, (path, _, mapping, duration, directory, options, budget) =>
            new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path));
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
