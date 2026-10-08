namespace AegiNext.Application.Tasks;

/// <summary>A reversible scope shutdown with a controlled final-persistence submission channel.</summary>
public sealed class AegiTaskScopeCloseLease : IDisposable, IAsyncDisposable
{
    private AegiTaskService? service;
    private readonly long generation;

    internal AegiTaskScopeCloseLease(AegiTaskService service, string scopeId, long generation)
    {
        this.service = service;
        ScopeId = scopeId;
        this.generation = generation;
    }

    /// <summary>Gets the project scope sealed by this lease.</summary>
    public string ScopeId { get; }

    /// <summary>Waits until the scope's submitted operations and cleanup finish.</summary>
    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        return GetService().DrainScopeAsync(ScopeId, generation, cancellationToken);
    }

    /// <summary>Submits required final persistence while ordinary scope submissions remain sealed.</summary>
    public AegiTaskHandle SubmitFinalization(AegiTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.ScopeId != ScopeId)
        {
            throw new ArgumentException("Finalization must belong to the sealed scope.", nameof(task));
        }

        return GetService().SubmitFinalization(task, ScopeId, generation);
    }

    /// <summary>Submits result-bearing required final persistence.</summary>
    public AegiTaskHandle<TResult> SubmitFinalization<TResult>(AegiTask<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.ScopeId != ScopeId)
        {
            throw new ArgumentException("Finalization must belong to the sealed scope.", nameof(task));
        }

        return GetService().SubmitFinalization(task, ScopeId, generation);
    }

    /// <summary>Marks the drained scope permanently closed.</summary>
    public void CompleteClose()
    {
        var owner = GetService();
        owner.CompleteScopeClose(ScopeId, generation);
        Interlocked.Exchange(ref service, null);
    }

    /// <summary>Reopens a scope when closing was abandoned or final persistence failed.</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref service, null)?.ReopenScope(ScopeId, generation);
    }

    /// <summary>Reopens a scope asynchronously when the lease was not completed.</summary>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    private AegiTaskService GetService()
    {
        return Volatile.Read(ref service) ?? throw new ObjectDisposedException(nameof(AegiTaskScopeCloseLease));
    }
}
