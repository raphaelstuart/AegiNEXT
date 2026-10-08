namespace AegiNext.Application.Tasks;

/// <summary>A task identity with independent waiting and cancellation operations.</summary>
public class AegiTaskHandle
{
    private Func<bool>? requestCancel;
    private AegiTaskSnapshot snapshot;

    internal AegiTaskHandle(Task<object?> completion, AegiTaskSnapshot snapshot, Func<bool> requestCancel)
    {
        CompletionCore = completion;
        this.snapshot = snapshot;
        this.requestCancel = requestCancel;
    }

    /// <summary>Gets the stable task identity.</summary>
    public Guid Id => Snapshot.Id;

    /// <summary>Gets the last immutable status of this operation.</summary>
    public AegiTaskSnapshot Snapshot => Volatile.Read(ref snapshot);

    /// <summary>Gets completion after the operation and its cleanup actually end.</summary>
    public Task Completion => CompletionCore;

    internal Task<object?> CompletionCore { get; }

    /// <summary>Waits for completion; cancelling this wait leaves the operation running.</summary>
    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        return Completion.WaitAsync(cancellationToken);
    }

    /// <summary>Explicitly requests operation cancellation before atomic commit.</summary>
    public bool RequestCancel()
    {
        return Volatile.Read(ref requestCancel)?.Invoke() ?? false;
    }

    internal void Update(AegiTaskSnapshot value)
    {
        Volatile.Write(ref snapshot, value);
        if (value.IsFinished)
        {
            Volatile.Write(ref requestCancel, null);
        }
    }
}
