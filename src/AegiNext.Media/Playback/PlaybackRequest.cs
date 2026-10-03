using AegiNext.Core.Timing;

namespace AegiNext.Media.Playback;

internal sealed class PlaybackRequest : IDisposable
{
    private readonly CancellationToken cancellationToken;
    private readonly CancellationTokenRegistration cancellationRegistration;
    private readonly TaskCompletionSource<VideoSeekResult?> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int state;

    internal PlaybackRequest(PlaybackOperation operation, long generation, MediaTime target, CancellationToken cancellationToken)
    {
        Operation = operation;
        Generation = generation;
        Target = target;
        this.cancellationToken = cancellationToken;
        cancellationRegistration = cancellationToken.UnsafeRegister(static value => ((PlaybackRequest)value!).CancelPending(), this);
    }

    internal PlaybackOperation Operation { get; }

    internal long Generation { get; }

    internal MediaTime Target { get; }

    internal Task<VideoSeekResult?> Completion => completion.Task;

    internal bool TryStart()
    {
        return Interlocked.CompareExchange(ref state, 1, 0) == 0;
    }

    internal void Complete(VideoSeekResult? result = null)
    {
        Interlocked.Exchange(ref state, 2);
        completion.TrySetResult(result);
    }

    internal void Fail(Exception exception)
    {
        Interlocked.Exchange(ref state, 2);
        completion.TrySetException(exception);
    }

    internal void Supersede()
    {
        Interlocked.Exchange(ref state, 2);
        completion.TrySetCanceled();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        cancellationRegistration.Dispose();
    }

    private void CancelPending()
    {
        if (Interlocked.CompareExchange(ref state, 3, 0) == 0)
        {
            completion.TrySetCanceled(cancellationToken);
        }
    }
}
