using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings.AudioAnalysis;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AudioAnalysisPreferencesWorkflowTests
{
    [Fact]
    public async Task ExecutionAndDisplayChangesKeepTheSessionAndPublishedLayers()
    {
        AudioAnalysisSession? current = null;
        AudioAnalysisWorkerBudget? sharedBudget = null;
        var opened = 0;
        await using var context = CreateContext((path, _, mapping, duration, directory, options, budget) =>
        {
            opened++;
            sharedBudget = budget;
            return current = new(_ => new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE), mapping, duration,
                cacheDirectory: directory, cacheIdentity: path, options: options, workerBudget: budget);
        });
        await context.Session.ApplicationContext.Initialization;
        await context.Session.Controller.OpenAsync("preferences-analysis.mkv");
        await context.Session.Analysis.StartAsync("preferences-analysis.mkv");
        await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var previous = current;
        var waveform = context.Session.ViewModel.Timeline.Waveform;
        var spectrum = context.Session.ViewModel.Timeline.Spectrogram;
        var changed = context.Session.Preferences.AudioAnalysis with
        {
            Execution = new() { MaximumWorkers = 1, MemoryBudgetMiB = 96, SegmentSamples = 98304 },
            Display = new() { WaveformGain = 2 }
        };

        context.Session.UpdatePreferences(value => value with { AudioAnalysis = changed });
        await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Same(previous, current);
        Assert.Same(waveform, context.Session.ViewModel.Timeline.Waveform);
        Assert.Same(spectrum, context.Session.ViewModel.Timeline.Spectrogram);
        Assert.Equal(changed.Execution, current!.Options.Execution);
        Assert.Same(context.Session.ApplicationContext.AudioAnalysisBudget, sharedBudget);
        Assert.Equal(1, sharedBudget!.MaximumWorkers);
        Assert.Equal(1, opened);
        Assert.DoesNotContain(context.Session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.Name == "Tasks.AudioAnalysisRebuild");
    }

    [Fact]
    public async Task ExplicitApplyRebuildsTheSameRecipeAndRetainsLayersUntilNewDataIsPublished()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var opened = 0;
        var sources = new List<AnalysisBudgetSource>();
        await using var context = CreateContext((path, _, mapping, duration, directory, options, budget) =>
        {
            var sequence = ++opened;
            var source = new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE)
            {
                BeforeRead = sequence == 2 ? token =>
                {
                    entered.TrySetResult();
                    release.Wait(token);
                } : null
            };
            sources.Add(source);
            return new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path,
                options: options, workerBudget: budget);
        });
        await context.Session.ApplicationContext.Initialization;
        await context.Session.Controller.OpenAsync("force-analysis.mkv");
        await context.Session.Analysis.StartAsync("force-analysis.mkv");
        await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var timeline = context.Session.ViewModel.Timeline;
        var waveform = timeline.Waveform;
        var spectrum = timeline.Spectrogram;
        try
        {
            await context.Session.ApplicationContext.RebuildAudioAnalysisAsync(context.Session.Preferences.AudioAnalysis)
                .WaitAsync(TimeSpan.FromSeconds(5));
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Same(waveform, timeline.Waveform);
            Assert.Same(spectrum, timeline.Spectrogram);
            Assert.Equal(1, sources[0].DisposeCount);
            release.Set();
            await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.NotSame(waveform, timeline.Waveform);
            Assert.NotSame(spectrum, timeline.Spectrogram);
            Assert.Equal(2, opened);
            Assert.Equal(4L * WaveformAnalyzer.SAMPLE_RATE, sources[1].FramesRead);
            Assert.Single(context.Session.ApplicationContext.Tasks.GetSnapshots(),
                value => value.Name == "Tasks.AudioAnalysisRebuild" && value.State == AegiTaskState.Succeeded);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task ApplyingRecipeCancelsAndDrainsTheOldBuildBeforeOpeningTheNewSessionAtOneSlot()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldSource = new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        var opened = 0;
        AudioAnalysisSession? current = null;
        await using var context = CreateContext((path, _, mapping, duration, directory, options, budget) =>
        {
            if (++opened == 2)
            {
                Assert.Equal(1, oldSource.DisposeCount);
            }
            return current = new(_ => opened == 1 ? oldSource : new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE),
                mapping, duration, cacheDirectory: directory, cacheIdentity: path, options: options, workerBudget: budget);
        });
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(value => value with { MaximumConcurrentTasks = 1 });
        await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Session.Controller.OpenAsync("active-recipe-analysis.mkv");
        try
        {
            await context.Session.Analysis.StartAsync("active-recipe-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var changed = context.Session.Preferences.AudioAnalysis with
            {
                AdvancedMode = true,
                Recipe = new() { FftSize = 2048, FrequencyBins = 256 }
            };
            await context.Session.ApplicationContext.RebuildAudioAnalysisAsync(changed).WaitAsync(TimeSpan.FromSeconds(5));
            await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(1, oldSource.CancelCount);
            Assert.Equal(1, oldSource.DisposeCount);
            Assert.Equal(2, opened);
            Assert.Equal(changed.Recipe, current!.Options.Recipe);
            Assert.Equal(256, context.Session.ViewModel.Timeline.Spectrogram!.Height);
            Assert.Single(context.Session.ApplicationContext.Tasks.GetSnapshots(),
                value => value.Name == "Tasks.AudioAnalysisRebuild" && value.State == AegiTaskState.Succeeded);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task RecipePreferenceUpdatesWaitForExplicitApplyAndPreserveThePreviousCache()
    {
        AudioAnalysisSession? current = null;
        var opened = 0;
        await using var context = CreateContext((path, _, mapping, duration, directory, options, budget) =>
        {
            opened++;
            return current = new(_ => new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE), mapping, duration,
                cacheDirectory: directory, cacheIdentity: path, options: options, workerBudget: budget);
        });
        await context.Session.ApplicationContext.Initialization;
        await context.Session.Controller.OpenAsync("recipe-cache-analysis.mkv");
        await context.Session.Analysis.StartAsync("recipe-cache-analysis.mkv");
        await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var original = current!;
        var originalDirectory = original.CacheDirectory;
        var changed = context.Session.Preferences.AudioAnalysis with { Recipe = new() { FftSize = 2048 } };

        context.Session.UpdatePreferences(value => value with { AudioAnalysis = changed });
        Assert.Same(original, current);
        Assert.NotEqual(changed.Recipe, current!.Options.Recipe);
        Assert.Equal(1, opened);
        await context.Session.ApplicationContext.RebuildAudioAnalysisAsync(changed);
        await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.NotSame(original, current);
        Assert.NotEqual(originalDirectory, current!.CacheDirectory);
        Assert.True(Directory.Exists(originalDirectory));
        Assert.Equal(changed.Recipe, current.Options.Recipe);
        Assert.Equal(2, opened);
    }

    [Fact]
    public async Task ExplicitApplyBroadcastsOneRebuildPerOpenProjectWithOneSharedBudget()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await using var application = new DesktopApplicationContext(new(directory.Path));
        await application.Initialization;
        var firstOpened = 0;
        var secondOpened = 0;
        var budgets = new List<AudioAnalysisWorkerBudget?>();
        await using var first = CreateContext((path, _, mapping, duration, cacheDirectory, options, budget) =>
        {
            firstOpened++;
            budgets.Add(budget);
            return new(_ => new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE), mapping, duration,
                cacheDirectory: cacheDirectory, cacheIdentity: path, options: options, workerBudget: budget);
        }, application);
        await using var second = CreateContext((path, _, mapping, duration, cacheDirectory, options, budget) =>
        {
            secondOpened++;
            budgets.Add(budget);
            return new(_ => new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE), mapping, duration,
                cacheDirectory: cacheDirectory, cacheIdentity: path, options: options, workerBudget: budget);
        }, application);
        await first.Session.Controller.OpenAsync("first-analysis.mkv");
        await second.Session.Controller.OpenAsync("second-analysis.mkv");
        await first.Session.Analysis.StartAsync("first-analysis.mkv");
        await second.Session.Analysis.StartAsync("second-analysis.mkv");
        await Task.WhenAll(first.Session.Analysis.Completion, second.Session.Analysis.Completion).WaitAsync(TimeSpan.FromSeconds(10));

        await application.RebuildAudioAnalysisAsync(application.Preferences.AudioAnalysis);
        await Task.WhenAll(first.Session.Analysis.Completion, second.Session.Analysis.Completion).WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, firstOpened);
        Assert.Equal(2, secondOpened);
        Assert.All(budgets, budget => Assert.Same(application.AudioAnalysisBudget, budget));
        var rebuilds = application.Tasks.GetSnapshots().Where(value => value.Name == "Tasks.AudioAnalysisRebuild").ToArray();
        Assert.Equal(2, rebuilds.Length);
        Assert.Contains(rebuilds, value => value.ScopeId == first.Session.TaskScope && value.State == AegiTaskState.Succeeded);
        Assert.Contains(rebuilds, value => value.ScopeId == second.Session.TaskScope && value.State == AegiTaskState.Succeeded);
    }

    [Fact]
    public async Task SwitchingMediaInvalidatesARebuildStillWaitingForOldDecoderCleanup()
    {
        using var release = new ManualResetEventSlim();
        using var cleanupRelease = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleanupEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                try
                {
                    release.Wait(token);
                }
                finally
                {
                    cleanupEntered.TrySetResult();
                    cleanupRelease.Wait(CancellationToken.None);
                }
            }
        };
        var paths = new List<string>();
        await using var context = CreateContext((path, _, mapping, duration, directory, options, budget) =>
        {
            paths.Add(path);
            return new(_ => path == "old-pending-analysis.mkv" ? source : new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE),
                mapping, duration, cacheDirectory: directory, cacheIdentity: path, options: options, workerBudget: budget);
        });
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(value => value with { MaximumConcurrentTasks = 1 });
        await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Session.Controller.OpenAsync("old-pending-analysis.mkv");
        Task? rebuild = null;
        Task? replacement = null;
        try
        {
            await context.Session.Analysis.StartAsync("old-pending-analysis.mkv");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var changed = context.Session.Preferences.AudioAnalysis with { Recipe = new() { FftSize = 2048 } };
            rebuild = context.Session.ApplicationContext.RebuildAudioAnalysisAsync(changed);
            await cleanupEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(rebuild.IsCompleted);
            replacement = context.Session.Analysis.StartAsync("latest-analysis.mkv");
            Assert.False(replacement.IsCompleted);
            cleanupRelease.Set();
            await Task.WhenAll(rebuild, replacement).WaitAsync(TimeSpan.FromSeconds(5));
            await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            var expected = new[] { "old-pending-analysis.mkv", "latest-analysis.mkv" };
            Assert.Equal(expected, paths);
            Assert.Equal(1, source.DisposeCount);
            Assert.DoesNotContain(context.Session.ApplicationContext.Tasks.GetSnapshots(),
                value => value.Name == "Tasks.AudioAnalysisRebuild");
            Assert.NotNull(context.Session.ViewModel.Timeline.Waveform);
        }
        finally
        {
            release.Set();
            cleanupRelease.Set();
            if (rebuild is not null)
            {
                await rebuild.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
            if (replacement is not null)
            {
                await replacement.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
            }
        }
    }

    [Fact]
    public async Task ApplyingInsideAParentTaskDefersTheNewBuildUntilTheParentReleasesItsSingleSlot()
    {
        var sources = new List<AnalysisBudgetSource>();
        await using var context = CreateContext((path, _, mapping, duration, directory, options, budget) =>
        {
            var source = new AnalysisBudgetSource(4L * WaveformAnalyzer.SAMPLE_RATE);
            sources.Add(source);
            return new(_ => source, mapping, duration, cacheDirectory: directory, cacheIdentity: path,
                options: options, workerBudget: budget);
        });
        await context.Session.ApplicationContext.Initialization;
        context.Session.UpdatePreferences(value => value with { MaximumConcurrentTasks = 1 });
        await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Session.Controller.OpenAsync("parent-rebuild-analysis.mkv");
        await context.Session.Analysis.StartAsync("parent-rebuild-analysis.mkv");
        await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var preferences = context.Session.Preferences.AudioAnalysis with { Recipe = new() { FftSize = 2048 } };
        var handle = context.Session.ApplicationContext.Tasks.Submit(
            new AnalysisRebuildRequestTask(context.Session, preferences, accepted, release.Task));
        try
        {
            await accepted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, context.Session.ApplicationContext.Tasks.MaximumConcurrentTasks);
            Assert.Equal(2, sources.Count);
            Assert.Equal(0, sources[1].FramesRead);
            Assert.Equal(1, sources[0].DisposeCount);
            release.TrySetResult();
            await handle.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            await context.Session.Analysis.Completion.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(4L * WaveformAnalyzer.SAMPLE_RATE, sources[1].FramesRead);
            Assert.Single(context.Session.ApplicationContext.Tasks.GetSnapshots(),
                value => value.Name == "Tasks.AudioAnalysisRebuild" && value.State == AegiTaskState.Succeeded);
        }
        finally
        {
            release.TrySetResult();
            await handle.Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        }
    }

    [Fact]
    public async Task ApplyingWithoutOpenMediaStillSavesTheAdvancedPreferences()
    {
        await using var context = CreateContext((_, _, _, _, _, _, _) => throw new InvalidOperationException("No media is open."));
        await context.Session.ApplicationContext.Initialization;
        var preferences = new AudioAnalysisPreferences { AdvancedMode = true, Recipe = new() { FftSize = 2048 } };

        await context.Session.ApplicationContext.RebuildAudioAnalysisAsync(preferences);
        await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(preferences, context.Session.Preferences.AudioAnalysis);
        Assert.Equal(preferences, context.Session.PreferencesStore.Load().AudioAnalysis);
        Assert.DoesNotContain(context.Session.ApplicationContext.Tasks.GetSnapshots(),
            value => value.Name is "Tasks.AudioAnalysis" or "Tasks.AudioAnalysisRebuild");
    }

    private static WorkspaceSessionTestContext CreateContext(
        Func<string, int, MediaTimelineMapping, MediaTime, string, AudioAnalysisOptions, AudioAnalysisWorkerBudget?, AudioAnalysisSession> factory,
        DesktopApplicationContext? application = null)
    {
        var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(4), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 100), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, target, _) => Task.FromResult(new AudioPlaybackSession(new CalibrationAudioSource(),
                new CalibrationAudioOutput("analysis-preferences-test"), target))), applicationContext: application,
            analysisSessionFactory: factory);
        context.Session.ViewModel.Timeline.Viewport = new(0, 80, Width: 640);
        return context;
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
