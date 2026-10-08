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
    private readonly Func<string, int, MediaTimelineMapping, MediaTime, AudioAnalysisSession> createSession;
    private readonly Lock jobsGate = new();
    private readonly HashSet<Task> jobs = [];
    private AudioAnalysisSession? analysis;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? windowCancellation;
    private WaveformViewportPlan? desired;
    private bool desiredWaveform;
    private bool desiredSpectrum;
    private bool isAnalyzing;
    private long epoch;
    private long revision;
    private bool analysisBatchPending;

    public Task Completion => DrainAsync();

    internal AnalysisCoordinator(WorkbenchSession session,
        Func<string, int, MediaTimelineMapping, MediaTime, AudioAnalysisSession>? createSession = null)
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
        cancellation?.Cancel();
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
            timeline.Waveform = null;
            timeline.Spectrogram = null;
            timeline.AnalysisStatus = string.Empty;
            return;
        }
        RefreshLanguage();
        if (!analysisBatchPending)
        {
            analysisBatchPending = true;
            var task = new AudioAnalysisBatchTask(session, this, current, epoch);
            if (AegiTaskExecutionContext.Current is { } parent)
            {
                parent.ScheduleAfterCompletion(task, handle =>
                {
                    if (handle is null)
                    {
                        if (ReferenceEquals(analysis, current))
                        {
                            analysisBatchPending = false;
                        }
                    }
                    else
                    {
                        Track(ObserveBatchAsync(handle, current, task.Epoch));
                    }
                });
            }
            else
            {
                Track(ObserveBatchAsync(session.ApplicationContext.Tasks.Submit(task), current, epoch));
            }
        }
    }

    private async Task ObserveBatchAsync(AegiTaskHandle handle, AudioAnalysisSession current, long requestEpoch)
    {
        await handle.Completion.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (handle.Completion.IsCanceled)
        {
            var disposeCurrent = false;
            await session.DispatchTaskCompletionAsync(() =>
            {
                if (requestEpoch == epoch && ReferenceEquals(analysis, current))
                {
                    windowCancellation?.Cancel();
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
    }

    internal async Task ExecuteBatchAsync(AudioAnalysisSession current, long requestEpoch, AegiTaskExecutionContext context)
    {
        try
        {
            while (IsCurrent(requestEpoch, context.CancellationToken) && desired is { } plan && windowCancellation is { } window)
            {
                var requestedRevision = revision;
                using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(window.Token, context.CancellationToken);
                context.ReportProgress(new(desiredSpectrum ? "Tasks.Spectrum" : "Tasks.Waveform"));
                await AnalyzeWindowAsync(current, plan.Analysis, desiredWaveform, desiredSpectrum,
                    requestEpoch, requestedRevision, false, lifetime.Token);
                context.CancellationToken.ThrowIfCancellationRequested();
                if (requestedRevision == revision)
                {
                    break;
                }
            }
        }
        finally
        {
            if (requestEpoch == epoch)
            {
                analysisBatchPending = false;
                if (context.CancellationToken.IsCancellationRequested)
                {
                    windowCancellation?.Cancel();
                    cancellation?.Cancel();
                    if (ReferenceEquals(analysis, current))
                    {
                        analysis = null;
                    }
                    await current.DisposeAsync();
                    desired = null;
                    isAnalyzing = false;
                    session.ViewModel.Timeline.AnalysisStatus = string.Empty;
                }
            }
        }
    }

    private async Task AnalyzeWindowAsync(AudioAnalysisSession current, WaveformAnalysisRequest request, bool waveform, bool spectrum,
        long requestEpoch, long requestRevision, bool immediate, CancellationToken token)
    {
        var published = false;
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
            var timeline = session.ViewModel.Timeline;
            if (spectrum)
            {
                var result = await current.GetLayersAsync(request, false, true, token);
                if (!IsCurrent(requestEpoch, token) || requestRevision != revision)
                {
                    return;
                }
                timeline.Spectrogram = result.Spectrogram;
            }
            if (waveform)
            {
                var result = await current.GetLayersAsync(request, true, false, token);
                if (!IsCurrent(requestEpoch, token) || requestRevision != revision)
                {
                    return;
                }
                timeline.Waveform = result.Waveform;
            }
            timeline.AnalysisStatus = string.Empty;
            published = true;
        }
        catch (OperationCanceledException)
        {
            if (IsCurrent(requestEpoch, token) && requestRevision == revision)
            {
                session.ViewModel.Timeline.AnalysisStatus = string.Empty;
            }
        }
        catch (Exception error)
        {
            ReportFailure(error, requestEpoch, token);
            throw;
        }
        finally
        {
            if (requestEpoch == epoch && requestRevision == revision)
            {
                isAnalyzing = false;
                if (!published)
                {
                    desired = null;
                }
            }
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
        cancellation?.Dispose();
        cancellation = null;
        desired = null;
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
        var current = createSession(path, index, new(media.Start ?? MediaTime.Zero), duration);
        analysis = current;
        session.ViewModel.Timeline.AudioDuration = duration;
        RefreshWindow(true);
    }

    private bool IsCurrent(long requestEpoch, CancellationToken token) =>
        requestEpoch == epoch && !token.IsCancellationRequested && !session.IsClosing;

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
        try
        {
            await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }
        finally
        {
            lock (jobsGate)
            {
                jobs.Remove(task);
            }
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (jobsGate)
            {
                jobs.RemoveWhere(task => task.IsCompletedSuccessfully || task.IsCanceled);
                pending = jobs.ToArray();
            }
            if (pending.Length == 0)
            {
                return;
            }
            await Task.WhenAll(pending);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.ViewModel.Timeline.PropertyChanged -= OnTimelineChanged;
        Cancel();
        windowCancellation?.Dispose();
        cancellation?.Dispose();
    }
}
