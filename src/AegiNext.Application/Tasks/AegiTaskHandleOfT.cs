namespace AegiNext.Application.Tasks;

/// <summary>A task handle that exposes an operation result.</summary>
public sealed class AegiTaskHandle<TResult> : AegiTaskHandle
{
    internal AegiTaskHandle(Task<object?> completion, AegiTaskSnapshot snapshot, Func<bool> requestCancel)
        : base(completion, snapshot, requestCancel)
    {
        Completion = ConvertAsync(completion);
    }

    /// <summary>Gets the accepted result after execution and cleanup finish.</summary>
    public new Task<TResult> Completion { get; }

    /// <summary>Waits for the result without transferring cancellation to the operation.</summary>
    public new Task<TResult> WaitAsync(CancellationToken cancellationToken = default)
    {
        return Completion.WaitAsync(cancellationToken);
    }

    private static async Task<TResult> ConvertAsync(Task<object?> completion)
    {
        return (TResult)(await completion.ConfigureAwait(false))!;
    }
}
