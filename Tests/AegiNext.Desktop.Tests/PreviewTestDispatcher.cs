using System.Collections.Concurrent;

namespace AegiNext.Desktop.Tests;

internal sealed class PreviewTestDispatcher
{
    private readonly ConcurrentQueue<PendingPreviewDispatch> pending = new();
    private int blockNext;

    internal TaskCompletionSource Queued { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int PendingCount => pending.Count;

    internal void BlockNextDispatch()
    {
        Volatile.Write(ref blockNext, 1);
    }

    internal Task DispatchAsync(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref blockNext, 0) == 0)
        {
            action();
            return Task.CompletedTask;
        }

        var callback = new PendingPreviewDispatch(action);
        pending.Enqueue(callback);
        Queued.TrySetResult();
        return callback.Completion.WaitAsync(cancellationToken);
    }

    internal void RunPending()
    {
        while (pending.TryDequeue(out var callback))
        {
            callback.Run();
        }
    }
}
