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
    private readonly Lock jobsGate = new();
    private readonly HashSet<Task> jobs = [];
    private AudioAnalysisSession? analysis;
    private CancellationTokenSource? cancellation;
    private CancellationTokenSource? windowCancellation;
    private WaveformViewportPlan? desired;
    private bool desiredSpectrum;
    private bool isAnalyzing;
    private long epoch;
    private long revision;

    public Task Completion => DrainAsync();

    internal AnalysisCoordinator(WorkbenchSession session)
    {
        this.session = session;
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
        var plan = timeline.IsWaveformVisible || timeline.IsSpectrumVisible
            ? WaveformViewportPlanner.Create(timeline.Viewport, timeline.RenderScaling, current.Duration) : null;
        if (!immediate && plan == desired && desiredSpectrum == timeline.IsSpectrumVisible)
        {
            return;
        }
        desired = plan;
        desiredSpectrum = timeline.IsSpectrumVisible;
        revision++;
        windowCancellation?.Cancel();
        windowCancellation?.Dispose();
        windowCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);
        if (plan is null)
        {
            timeline.Waveform = null;
            timeline.Spectrogram = null;
            return;
        }
        Track(AnalyzeWindowAsync(current, plan.Analysis, desiredSpectrum, epoch, revision, immediate, windowCancellation.Token));
    }

    private async Task AnalyzeWindowAsync(AudioAnalysisSession current, WaveformAnalysisRequest request, bool spectrum,
        long requestEpoch, long requestRevision, bool immediate, CancellationToken token)
    {
        try
        {
            if (!immediate)
            {
                await Task.Delay(75, token);
            }
            var result = await current.GetWindowAsync(request, spectrum, token);
            if (IsCurrent(requestEpoch, token) && requestRevision == revision)
            {
                var timeline = session.ViewModel.Timeline;
                timeline.Waveform = result.Waveform;
                timeline.Spectrogram = result.Spectrogram;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            ReportFailure(error, requestEpoch, token);
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
        var current = AudioAnalysisSession.Open(path, index, new(media.Start ?? MediaTime.Zero), duration);
        analysis = current;
        session.ViewModel.Timeline.AudioDuration = duration;
        isAnalyzing = true;
        RefreshLanguage();
        RefreshWindow(true);
        Track(AnalyzeOverviewAsync(current, path, epoch, cancellation.Token));
    }

    private async Task AnalyzeOverviewAsync(AudioAnalysisSession current, string path, long requestEpoch, CancellationToken token)
    {
        try
        {
            var result = await current.GetOverviewAsync(WaveformViewportPlanner.CreateOverview(current.Duration), true, token);
            if (IsCurrent(requestEpoch, token))
            {
                var timeline = session.ViewModel.Timeline;
                timeline.WaveformOverview = result.Waveform;
                timeline.SpectrogramOverview = result.Spectrogram;
                timeline.AnalysisStatus = string.Empty;
                session.LogInfo("Analysis", Localization.Get("WorkflowLog.AudioAnalysisCompleted"), path);
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
            if (requestEpoch == epoch)
            {
                isAnalyzing = false;
            }
        }
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
