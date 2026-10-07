namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisWorkItem(WaveformAnalysisRequest request, bool spectrum, long revision,
    bool overview, CancellationToken cancellationToken, bool waveform = true)
{
    internal WaveformAnalysisRequest Request { get; } = request;
    internal bool Spectrum { get; } = spectrum;
    internal bool Waveform { get; } = waveform;
    internal long Revision { get; } = revision;
    internal bool IsOverview { get; } = overview;
    internal CancellationToken Token { get; } = cancellationToken;
    internal TaskCompletionSource<AudioAnalysisLayers> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Cancel()
    {
        Completion.TrySetCanceled(Token.IsCancellationRequested ? Token : new CancellationToken(true));
    }
}
