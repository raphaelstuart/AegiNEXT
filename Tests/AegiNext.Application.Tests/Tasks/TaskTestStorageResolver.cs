using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

internal sealed class TaskTestStorageResolver
{
    private readonly TaskCompletionSource<AegiTaskResource> entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<AegiTaskResource> resolution = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource returned = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task<AegiTaskResource> Entered => entered.Task;

    internal Task Returned => returned.Task;

    internal SynchronizationContext? ExecutionSynchronizationContext { get; private set; }

    internal bool ExecutedOnThreadPool { get; private set; }

    internal bool BlockSynchronously { get; init; }

    internal async Task<AegiTaskResource> ResolveAsync(AegiTaskResource resource)
    {
        ExecutionSynchronizationContext = SynchronizationContext.Current;
        ExecutedOnThreadPool = Thread.CurrentThread.IsThreadPoolThread;
        entered.TrySetResult(resource);
        try
        {
            if (BlockSynchronously)
            {
                return resolution.Task.GetAwaiter().GetResult();
            }

            return await resolution.Task.ConfigureAwait(false);
        }
        finally
        {
            returned.TrySetResult();
        }
    }

    internal void Complete(AegiTaskResource resource)
    {
        resolution.TrySetResult(resource);
    }

    internal void Fail(Exception exception)
    {
        resolution.TrySetException(exception);
    }
}
