namespace AegiNext.Application.Tasks;

/// <summary>Schedules application operations and owns their observable lifecycle.</summary>
public interface IAegiTaskService : IAsyncDisposable
{
    /// <summary>Notifies observers of status or editing lease changes on any thread.</summary>
    event EventHandler? Changed;

    /// <summary>Gets or sets the execution limit, between one and thirty-two.</summary>
    int MaximumConcurrentTasks { get; set; }

    /// <summary>Registers an owning project scope and its display name.</summary>
    void RegisterScope(string scopeId, string displayName);

    /// <summary>Submits an operation on the caller's synchronization context.</summary>
    AegiTaskHandle Submit(AegiTask task);

    /// <summary>Submits an operation with a result.</summary>
    AegiTaskHandle<TResult> Submit<TResult>(AegiTask<TResult> task);

    /// <summary>Returns active operations followed by recent finished operations.</summary>
    IReadOnlyList<AegiTaskSnapshot> GetSnapshots();

    /// <summary>Gets whether any active lease restricts the specified project.</summary>
    bool IsEditingRestricted(string scopeId);

    /// <summary>Acquires a scope editing lease for synchronous UI interactions.</summary>
    AegiTaskEditLease AcquireScopeEditLease(string scopeId);

    /// <summary>Waits for current scope tasks without sealing new submissions.</summary>
    Task DrainScopeAsync(string scopeId, CancellationToken cancellationToken = default);

    /// <summary>Seals the scope and requests cancellation of its cancellable operations.</summary>
    AegiTaskScopeCloseLease BeginCloseScope(string scopeId);

    /// <summary>Explicitly requests operation cancellation by its stable identity.</summary>
    bool RequestCancel(Guid taskId);

    /// <summary>Clears finished records without affecting active operations.</summary>
    void ClearHistory();

    /// <summary>Waits for all currently submitted operations to finish.</summary>
    Task DrainAsync(CancellationToken cancellationToken = default);
}
