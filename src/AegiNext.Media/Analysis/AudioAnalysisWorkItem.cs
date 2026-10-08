namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisWorkItem(WaveformAnalysisRequest request, long revision, CancellationToken cancellationToken)
{
    internal WaveformAnalysisRequest Request { get; } = request;
    internal long Revision { get; } = revision;
    internal CancellationToken Token { get; } = cancellationToken;
    internal TaskCompletionSource<AudioAnalysisLayers> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal void Cancel()
    {
        Completion.TrySetCanceled(Token.IsCancellationRequested ? Token : new CancellationToken(true));
    }
}
