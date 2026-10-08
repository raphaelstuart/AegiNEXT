namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisWorkerRequest(AudioAnalysisWorkerBudget budget, object owner, bool foreground,
    CancellationToken cancellationToken)
{
    internal AudioAnalysisWorkerBudget Budget { get; } = budget;
    internal object Owner { get; } = owner;
    internal bool Foreground { get; } = foreground;
    internal CancellationToken CancellationToken { get; } = cancellationToken;
    internal TaskCompletionSource<AudioAnalysisWorkerLease> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal LinkedListNode<AudioAnalysisWorkerRequest>? Node { get; set; }
}
