using System.ComponentModel;
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
        Track(AnalyzeWindowAsync(current, plan.Analysis, desiredWaveform, desiredSpectrum, epoch, revision, immediate,
            windowCancellation.Token));
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
            var result = await current.GetLayersAsync(request, waveform, spectrum, token);
            if (IsCurrent(requestEpoch, token) && requestRevision == revision)
            {
                var timeline = session.ViewModel.Timeline;
                timeline.Waveform = result.Waveform;
                timeline.Spectrogram = result.Spectrogram;
                timeline.AnalysisStatus = string.Empty;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            ReportFailure(error, requestEpoch, token);
        }
        finally
        {
            if (requestEpoch == epoch && requestRevision == revision)
            {
                isAnalyzing = false;
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
            await task.ConfigureAwait(false);
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
