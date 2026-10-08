using System.Collections.Concurrent;

namespace AegiNext.Application.Tests.Tasks;

internal sealed class TaskTestSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();

    public override void Post(SendOrPostCallback d, object? state)
    {
        callbacks.Enqueue((d, state));
    }

    internal void RunPostedCallbacks()
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            while (callbacks.TryDequeue(out var item))
            {
                item.Callback(item.State);
            }
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }
}
