using AegiNext.Core.Timing;
using AegiNext.Desktop.Localization;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class AnalysisCoordinator(WorkbenchSession session) : IDisposable
{
    private CancellationTokenSource? cancellation;
    public Task Completion { get; private set; } = Task.CompletedTask;

    internal void Cancel() => cancellation?.Cancel();

    internal async Task ClearAsync()
    {
        cancellation?.Cancel();
        await Completion;
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
            return;
        }

        session.ViewModel.Timeline.AnalysisStatus = WorkbenchText.Get("Analyzing");
        Completion = AnalyzeAsync(path, index, media.Start ?? MediaTime.Zero, duration, cancellation.Token);
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
            }
        }
    }

    /// <inheritdoc />
    public void Dispose() => cancellation?.Dispose();
}
