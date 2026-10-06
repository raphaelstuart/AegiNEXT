using System.ComponentModel;
using AegiNext.Core.Timing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class AnalysisCoordinator : IDisposable
{
    private readonly WorkbenchSession session;
    private readonly WaveformAnalysisCoordinator waveform;
    private CancellationTokenSource? cancellation;
    private Task spectrogramCompletion = Task.CompletedTask;
    private bool isAnalyzing;
    public Task Completion => Task.WhenAll(spectrogramCompletion, waveform.Completion);

    internal AnalysisCoordinator(WorkbenchSession session)
    {
        this.session = session;
        waveform = new(PublishWaveform, error => session.LogError("Analysis", error));
        session.ViewModel.Timeline.PropertyChanged += OnTimelineChanged;
    }

    internal void Cancel()
    {
        cancellation?.Cancel();
        waveform.Cancel();
    }

    private void OnTimelineChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is nameof(TimelinePanelViewModel.Viewport) or nameof(TimelinePanelViewModel.RenderScaling) or
            nameof(TimelinePanelViewModel.IsWaveformVisible))
        {
            RefreshWaveform();
        }
    }

    private void RefreshWaveform()
    {
        var timeline = session.ViewModel.Timeline;
        waveform.Request(timeline.Viewport, timeline.RenderScaling, timeline.IsWaveformVisible);
    }

    private void PublishWaveform(WaveformData? detail, WaveformData? overview, MediaTime duration)
    {
        if (!session.IsClosing)
        {
            var timeline = session.ViewModel.Timeline;
            timeline.AudioDuration = duration;
            timeline.WaveformOverview = overview;
            timeline.Waveform = detail;
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
        await waveform.ClearAsync();
        session.ViewModel.Timeline.Spectrogram = null;
        session.ViewModel.Timeline.AnalysisStatus = string.Empty;
    }

    internal async Task StartAsync(string path)
    {
        await ClearAsync();
        cancellation?.Dispose();
        cancellation = new();
        var media = session.Controller.MediaInfo;
        if (media?.AudioStreamIndex is not { } index || media.Duration is not { } duration || duration <= MediaTime.Zero)
        {
            session.LogInfo("Analysis", Localization.Get("WorkflowLog.AudioAnalysisSkipped"), path);
            return;
        }

        isAnalyzing = true;
        RefreshLanguage();
        var origin = media.Start ?? MediaTime.Zero;
        await waveform.StartAsync((request, token) => WaveformAnalyzer.AnalyzeFileAsync(path, index, origin, request, token), duration);
        RefreshWaveform();
        spectrogramCompletion = AnalyzeAsync(path, index, origin, duration, cancellation.Token);
    }

    private async Task AnalyzeAsync(string path, int index, MediaTime start, MediaTime duration, CancellationToken token)
    {
        try
        {
            var data = await SpectrogramAnalyzer.AnalyzeFileAsync(path, index, start, duration, token);
            if (!token.IsCancellationRequested && !session.IsClosing)
            {
                session.ViewModel.Timeline.Spectrogram = data;
                session.ViewModel.Timeline.AnalysisStatus = string.Empty;
                session.LogInfo("Analysis", Localization.Get("WorkflowLog.AudioAnalysisCompleted"), path);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested && !session.IsClosing)
            {
                session.ViewModel.Timeline.AnalysisStatus = error.Message;
                session.LogError("Analysis", error);
            }
        }
        finally
        {
            isAnalyzing = false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        session.ViewModel.Timeline.PropertyChanged -= OnTimelineChanged;
        waveform.Dispose();
        cancellation?.Dispose();
    }
}
