using System.ComponentModel;
using AegiNext.Application.Tasks;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class AnalysisCoordinator : IDisposable
{
    private readonly WorkbenchSession session;
    private readonly Func<string, int, MediaTimelineMapping, MediaTime, string, AudioAnalysisOptions, AudioAnalysisWorkerBudget?, AudioAnalysisSession> createSession;
    private readonly Lock jobsGate = new();
    private readonly HashSet<Task> jobs = [];
    private readonly HashSet<AegiTaskHandle> handles = [];
    private readonly HashSet<AegiTaskHandle> migrationHandles = [];
    private readonly HashSet<AegiTask> migrationTasks = [];
    private AudioAnalysisSession? analysis;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? windowCancellation;
    private CancellationTokenSource? migrationCancellation;
    private CancellationTokenSource? rebuildCancellation;
    private WaveformViewportPlan? desired;
    private string? desiredCacheRoot;
    private string? mediaPath;
    private bool desiredWaveform;
    private bool desiredSpectrum;
    private bool isAnalyzing;
    private bool analysisBatchPending;
    private long epoch;
    private long revision;
    private long migrationRevision;

    public Task Completion => DrainAsync();

    internal AnalysisCoordinator(WorkbenchSession session,
        Func<string, int, MediaTimelineMapping, MediaTime, string, AudioAnalysisOptions, AudioAnalysisWorkerBudget?, AudioAnalysisSession>? createSession = null)
    {
        this.session = session;
        this.createSession = createSession ?? AudioAnalysisSession.Open;
        session.ViewModel.Timeline.PropertyChanged += OnTimelineChanged;
    }

    internal void Cancel()
    {
        epoch++;
        analysisBatchPending = false;
        windowCancellation?.Cancel();
        migrationCancellation?.Cancel();
        rebuildCancellation?.Cancel();
        cancellation?.Cancel();
        AegiTaskHandle[] pending;
        lock (jobsGate)
        {
            pending = [.. handles];
        }
        foreach (var handle in pending)
        {
            handle.RequestCancel();
        }
        if (analysis is { } current)
        {
            analysis = null;
            Track(current.DisposeAsync().AsTask());
        }
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TimelinePanelViewModel.Viewport) or nameof(TimelinePanelViewModel.RenderScaling) or
            nameof(TimelinePanelViewModel.IsWaveformVisible) or nameof(TimelinePanelViewModel.IsSpectrumVisible))
        {
            RefreshWindow();
        }
    }

    private void RefreshWindow(bool immediate = false)
    {
        if (analysis is not { } current || cancellation is null)
        {
            return;
        }
        var timeline = session.ViewModel.Timeline;
        if (!timeline.IsWaveformVisible)
        {
            timeline.Waveform = null;
        }
        if (!timeline.IsSpectrumVisible)
        {
            timeline.Spectrogram = null;
        }
        var plan = timeline.IsWaveformVisible || timeline.IsSpectrumVisible
            ? WaveformViewportPlanner.Create(timeline.Viewport, timeline.RenderScaling, current.Duration) : null;
        if (!immediate && plan == desired && desiredWaveform == timeline.IsWaveformVisible &&
            desiredSpectrum == timeline.IsSpectrumVisible)
        {
            return;
        }
        desired = plan;
        desiredWaveform = timeline.IsWaveformVisible;
        desiredSpectrum = timeline.IsSpectrumVisible;
        revision++;
        windowCancellation?.Cancel();
        windowCancellation?.Dispose();
        windowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        isAnalyzing = plan is not null;
        if (plan is null)
        {
            timeline.AnalysisStatus = string.Empty;
        }
        else
        {
            RefreshLanguage();
            Track(AnalyzeWindowAsync(current, plan.Analysis, desiredWaveform, desiredSpectrum,
                epoch, revision, immediate, windowCancellation.Token));
        }
        if (!analysisBatchPending && !current.IsCacheComplete)
        {
            ScheduleBuild(current);
        }
    }

    private void ScheduleBuild(AudioAnalysisSession current)
    {
        analysisBatchPending = true;
        var requestEpoch = epoch;
        var task = new AudioAnalysisBatchTask(session, this, current, requestEpoch);
        Schedule(task, current, requestEpoch, false);
    }

    private void Schedule(AegiTask task, AudioAnalysisSession current, long requestEpoch, bool migration)
    {
        if (migration)
        {
            lock (jobsGate)
            {
                migrationTasks.Add(task);
            }
        }
        try
        {
            if (AegiTaskExecutionContext.Current is { } parent)
            {
                parent.ScheduleAfterCompletion(task, handle => Register(handle, task, current, requestEpoch, migration));
            }
            else
            {
                Register(session.ApplicationContext.Tasks.Submit(task), task, current, requestEpoch, migration);
            }
        }
        catch
        {
            lock (jobsGate)
            {
                migrationTasks.Remove(task);
            }
            throw;
        }
    }

    private void Register(AegiTaskHandle? handle, AegiTask task, AudioAnalysisSession current, long requestEpoch, bool migration)
    {
        if (handle is null)
        {
            lock (jobsGate)
            {
                migrationTasks.Remove(task);
            }
            if (!migration && requestEpoch == epoch && ReferenceEquals(analysis, current))
            {
                analysisBatchPending = false;
            }
            return;
        }
        lock (jobsGate)
        {
            handles.Add(handle);
            if (migration)
            {
                migrationHandles.Add(handle);
            }
        }
        if (requestEpoch != epoch || !ReferenceEquals(analysis, current) ||
            task is AudioCacheMigrationTask migrationTask && migrationTask.Revision != migrationRevision)
        {
            handle.RequestCancel();
        }
        Track(ObserveAsync(handle, task, current, requestEpoch, migration));
    }

    private async Task ObserveAsync(AegiTaskHandle handle, AegiTask task, AudioAnalysisSession current, long requestEpoch, bool migration)
    {
        await handle.Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (jobsGate)
        {
            handles.Remove(handle);
            migrationHandles.Remove(handle);
            migrationTasks.Remove(task);
        }
        if (!migration && handle.Completion.IsCanceled)
        {
            await StopCancelledBuildAsync(current, requestEpoch);
        }
    }

    internal async Task ExecuteBatchAsync(AudioAnalysisSession current, long requestEpoch, AegiTaskExecutionContext context)
    {
        if (!IsCurrent(requestEpoch, context.CancellationToken) || !ReferenceEquals(analysis, current) || cancellation is null)
        {
            return;
        }
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token, context.CancellationToken);
        try
        {
            context.ReportProgress(new("Tasks.AudioAnalysis"));
            await current.PrepareCacheAsync(async token =>
            {
                await session.DispatchTaskCompletionAsync(() =>
                {
                    if (IsCurrent(requestEpoch, token) && ReferenceEquals(analysis, current))
                    {
                        RefreshWindow(true);
                    }
                });
                await context.YieldIfWorkIsQueuedAsync();
            }, new Progress<AudioAnalysisProgress>(progress =>
            {
                context.ReportProgress(new("Tasks.AudioAnalysis", (double)progress.Processed.Numerator / progress.Processed.Denominator,
                    (double)progress.Duration.Numerator / progress.Duration.Denominator));
            }), lifetime.Token);
            await session.DispatchTaskCompletionAsync(() =>
            {
                if (IsCurrent(requestEpoch, lifetime.Token) && ReferenceEquals(analysis, current))
                {
                    analysisBatchPending = false;
                    RefreshWindow(true);
                    ScheduleMigration(current, requestEpoch);
                }
            });
        }
        catch (OperationCanceledException)
        {
            await StopCancelledBuildAsync(current, requestEpoch);
            throw;
        }
        catch (Exception error)
        {
            await session.DispatchTaskCompletionAsync(() =>
            {
                if (IsCurrent(requestEpoch, lifetime.Token) && ReferenceEquals(analysis, current))
                {
                    analysisBatchPending = false;
                    desired = null;
                    isAnalyzing = false;
                    ReportFailure(error, requestEpoch, lifetime.Token);
                }
            });
            throw;
        }
    }

    private async Task StopCancelledBuildAsync(AudioAnalysisSession current, long requestEpoch)
    {
        var disposeCurrent = false;
        await session.DispatchTaskCompletionAsync(() =>
        {
            if (requestEpoch == epoch && ReferenceEquals(analysis, current))
            {
                windowCancellation?.Cancel();
                migrationCancellation?.Cancel();
                cancellation?.Cancel();
                analysis = null;
                desired = null;
                isAnalyzing = false;
                analysisBatchPending = false;
                session.ViewModel.Timeline.AnalysisStatus = string.Empty;
                disposeCurrent = true;
            }
        });
        if (disposeCurrent)
        {
            await current.DisposeAsync();
        }
    }

    private async Task AnalyzeWindowAsync(AudioAnalysisSession current, WaveformAnalysisRequest request, bool waveform, bool spectrum,
        long requestEpoch, long requestRevision, bool immediate, CancellationToken token)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(75, token);
            }
            if (!IsCurrent(requestEpoch, token) || requestRevision != revision)
            {
                return;
            }
            if (spectrum)
            {
                var result = await current.GetAvailableLayersAsync(request, false, true, token);
                await session.DispatchTaskCompletionAsync(() =>
                {
                    if (IsCurrent(requestEpoch, token) && requestRevision == revision && ReferenceEquals(analysis, current) &&
                        result.Spectrogram is { } spectrogram)
                    {
                        session.ViewModel.Timeline.Spectrogram = spectrogram;
                    }
                });
            }
            var waveformResult = waveform ? await current.GetAvailableLayersAsync(request, true, false, token) : null;
            await session.DispatchTaskCompletionAsync(() =>
            {
                if (!IsCurrent(requestEpoch, token) || requestRevision != revision || !ReferenceEquals(analysis, current))
                {
                    return;
                }
                var timeline = session.ViewModel.Timeline;
                if (waveformResult?.Waveform is { } waveformData)
                {
                    timeline.Waveform = waveformData;
                }
                if (current.IsCacheComplete)
                {
                    isAnalyzing = false;
                    timeline.AnalysisStatus = string.Empty;
                }
                else if (analysisBatchPending)
                {
                    isAnalyzing = true;
                    timeline.AnalysisStatus = Localization.Get("Workbench.Analyzing");
                }
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            await session.DispatchTaskCompletionAsync(() =>
            {
                if (requestRevision == revision && IsCurrent(requestEpoch, token) && ReferenceEquals(analysis, current))
                {
                    desired = null;
                    isAnalyzing = false;
                    ReportFailure(error, requestEpoch, token);
                    if (error is AudioAnalysisCacheCorruptionException && !analysisBatchPending)
                    {
                        QueueCacheRebuild(current, requestEpoch, requestRevision, token);
                    }
                }
            });
        }
    }

    private void QueueCacheRebuild(AudioAnalysisSession current, long requestEpoch, long requestRevision, CancellationToken token)
    {
        Task rebuild;
        using (ExecutionContext.SuppressFlow())
        {
            rebuild = Task.Run(() => session.DispatchTaskCompletionAsync(() =>
            {
                if (requestRevision == revision && IsCurrent(requestEpoch, token) && ReferenceEquals(analysis, current) &&
                    !analysisBatchPending)
                {
                    isAnalyzing = true;
                    RefreshLanguage();
                    ScheduleBuild(current);
                }
            }), CancellationToken.None);
        }
        Track(rebuild);
    }

    internal void ProjectDirectoryChanged(string directory)
    {
        desiredCacheRoot = Path.Combine(directory, "caches", "audio");
        migrationRevision++;
        migrationCancellation?.Cancel();
        migrationCancellation?.Dispose();
        migrationCancellation = null;
        AegiTaskHandle[] pending;
        bool migrationInFlight;
        lock (jobsGate)
        {
            pending = [.. migrationHandles];
            migrationInFlight = migrationTasks.Count > 0;
        }
        foreach (var handle in pending)
        {
            handle.RequestCancel();
        }
        if (analysis is { IsCacheComplete: true } current && !analysisBatchPending)
        {
            ScheduleMigration(current, epoch, migrationInFlight);
        }
    }

    private void ScheduleMigration(AudioAnalysisSession current, long requestEpoch, bool migrationInFlight = false)
    {
        if (desiredCacheRoot is not { } destination || cancellation is null ||
            !migrationInFlight && WorkbenchSession.PathsEqual(Path.GetDirectoryName(current.CacheDirectory), destination))
        {
            return;
        }
        migrationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        var task = new AudioCacheMigrationTask(session, this, current, requestEpoch, migrationRevision,
            destination, migrationCancellation.Token);
        Schedule(task, current, requestEpoch, true);
    }

    internal async Task ExecuteMigrationAsync(AudioAnalysisSession current, long requestEpoch, long requestMigrationRevision,
        string destination, AegiTaskExecutionContext context, CancellationToken token)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, context.CancellationToken);
        if (!IsCurrent(requestEpoch, lifetime.Token) || requestMigrationRevision != migrationRevision ||
            !ReferenceEquals(analysis, current))
        {
            return;
        }
        try
        {
            await current.RelocateCacheAsync(destination, _ => context.YieldIfWorkIsQueuedAsync(), lifetime.Token);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            await session.DispatchTaskCompletionAsync(() =>
            {
                if (requestEpoch == epoch && ReferenceEquals(analysis, current))
                {
                    session.LogError("Audio cache migration", error);
                }
            });
            throw;
        }
    }

    internal void RefreshLanguage()
    {
        if (isAnalyzing)
        {
            session.ViewModel.Timeline.AnalysisStatus = Localization.Get("Workbench.Analyzing");
        }
    }

    internal async Task ClearAsync()
    {
        Cancel();
        await Completion;
        windowCancellation?.Dispose();
        windowCancellation = null;
        migrationCancellation?.Dispose();
        migrationCancellation = null;
        cancellation?.Dispose();
        cancellation = null;
        desired = null;
        desiredCacheRoot = null;
        mediaPath = null;
        isAnalyzing = false;
        var timeline = session.ViewModel.Timeline;
        timeline.Waveform = null;
        timeline.WaveformOverview = null;
        timeline.Spectrogram = null;
        timeline.SpectrogramOverview = null;
        timeline.AudioDuration = MediaTime.Zero;
        timeline.AnalysisStatus = string.Empty;
    }

    internal async Task StartAsync(string path)
    {
        await ClearAsync();
        var media = session.Controller.MediaInfo;
        if (media?.AudioStreamIndex is not { } index || media.Duration is not { } duration || duration <= MediaTime.Zero)
        {
            session.LogInfo("Analysis", Localization.Get("WorkflowLog.AudioAnalysisSkipped"), path);
            return;
        }
        cancellation = new();
        var token = cancellation.Token;
        var requestEpoch = epoch;
        var directory = Path.Combine(session.ProjectDirectory, "caches", "audio");
        var preferences = session.Preferences.AudioAnalysis;
        var options = new AudioAnalysisOptions { Recipe = preferences.Recipe, Execution = preferences.Execution };
        var current = await Task.Run(() => createSession(path, index, new(media.Start ?? MediaTime.Zero), duration, directory,
            options, session.ApplicationContext.AudioAnalysisBudget), token);
        if (!IsCurrent(requestEpoch, token))
        {
            await current.DisposeAsync();
            return;
        }
        analysis = current;
        mediaPath = path;
        desiredCacheRoot = Path.Combine(session.ProjectDirectory, "caches", "audio");
        session.ViewModel.Timeline.AudioDuration = duration;
        RefreshWindow(true);
    }

    internal void UpdateExecutionOptions(AudioAnalysisExecutionOptions options)
    {
        analysis?.UpdateExecutionOptions(options);
    }

    internal Task RebuildAsync(AudioAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (mediaPath is not { } path || session.IsClosing)
        {
            return Task.CompletedTask;
        }
        var media = session.Controller.MediaInfo;
        if (media?.AudioStreamIndex is not { } index || media.Duration is not { } duration || duration <= MediaTime.Zero)
        {
            return Task.CompletedTask;
        }
        Cancel();
        Task[] previous;
        lock (jobsGate)
        {
            previous = [.. jobs];
        }
        rebuildCancellation?.Dispose();
        rebuildCancellation = new();
        var completion = RebuildAfterDrainAsync(previous, path, index, new(media.Start ?? MediaTime.Zero), duration,
            options, epoch, rebuildCancellation.Token);
        Track(completion);
        return completion;
    }

    private async Task RebuildAfterDrainAsync(Task[] previous, string path, int index, MediaTimelineMapping mapping,
        MediaTime duration, AudioAnalysisOptions options, long requestEpoch, CancellationToken token)
    {
        await Task.WhenAll(previous).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (!IsCurrent(requestEpoch, token))
        {
            return;
        }
        var directory = desiredCacheRoot ?? Path.Combine(session.ProjectDirectory, "caches", "audio");
        var current = await Task.Run(() => createSession(path, index, mapping, duration, directory,
            options, session.ApplicationContext.AudioAnalysisBudget), token);
        if (!IsCurrent(requestEpoch, token))
        {
            await current.DisposeAsync();
            return;
        }
        try
        {
            current.InvalidateCache();
        }
        catch
        {
            await current.DisposeAsync();
            throw;
        }
        cancellation?.Dispose();
        cancellation = new();
        analysis = current;
        desired = null;
        desiredCacheRoot ??= directory;
        analysisBatchPending = true;
        session.ViewModel.Timeline.AudioDuration = duration;
        Schedule(new RebuildAudioAnalysisTask(session, this, current, requestEpoch), current, requestEpoch, false);
        RefreshWindow(true);
    }

    private bool IsCurrent(long requestEpoch, CancellationToken token)
    {
        return requestEpoch == epoch && !token.IsCancellationRequested && !session.IsClosing;
    }

    private void ReportFailure(Exception error, long requestEpoch, CancellationToken token)
    {
        if (IsCurrent(requestEpoch, token))
        {
            session.ViewModel.Timeline.AnalysisStatus = error.Message;
            session.LogError("Analysis", error);
        }
    }

    private void Track(Task task)
    {
        lock (jobsGate)
        {
            jobs.Add(task);
        }
        _ = ForgetAsync(task);
    }

    private async Task ForgetAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (jobsGate)
        {
            jobs.Remove(task);
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (jobsGate)
            {
                pending = [.. jobs];
            }
            if (pending.Length == 0)
            {
                return;
            }
            await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.ViewModel.Timeline.PropertyChanged -= OnTimelineChanged;
        Cancel();
        windowCancellation?.Dispose();
        migrationCancellation?.Dispose();
        rebuildCancellation?.Dispose();
        cancellation?.Dispose();
    }
}
