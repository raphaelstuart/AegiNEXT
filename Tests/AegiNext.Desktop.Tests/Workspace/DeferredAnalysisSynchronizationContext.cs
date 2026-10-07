using System.Collections.Concurrent;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class DeferredAnalysisSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> callbacks = new();
    private readonly TaskCompletionSource posted = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Posted => posted.Task;

    /// <summary>保留已完成防抖的真实 continuation，允许测试在派发前提交更新的视口。</summary>
    public override void Post(SendOrPostCallback callback, object? state)
    {
        callbacks.Enqueue((callback, state));
        posted.TrySetResult();
    }

    internal void Capture(Action action)
    {
        var previous = Current;
        SetSynchronizationContext(this);
        try
        {
            action();
        }
        finally
        {
            SetSynchronizationContext(previous);
        }
    }

    internal void RunCallbacks()
    {
        while (callbacks.TryDequeue(out var work))
        {
            work.Callback(work.State);
        }
    }
}
