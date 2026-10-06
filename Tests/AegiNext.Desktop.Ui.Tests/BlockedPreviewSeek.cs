using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class BlockedPreviewSeek(MediaTime target)
{
    private readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal MediaTime Target { get; } = target;
    internal Task Entered => entered.Task;

    internal void Release()
    {
        released.TrySetResult();
    }

    internal void Wait(CancellationToken cancellationToken)
    {
        entered.TrySetResult();
        released.Task.Wait(cancellationToken);
    }
}
