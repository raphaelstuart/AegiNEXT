namespace AegiNext.Desktop.Tests;

internal sealed class PendingPreviewDispatch(Action action)
{
    private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal Task Completion => completion.Task;

    internal void Run()
    {
        try
        {
            action();
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
            throw;
        }
    }
}
