namespace AegiNext.Application.Tasks;

/// <summary>A business operation scheduled by the application task service.</summary>
public abstract class AegiTask
{
    /// <summary>Gets the display text or localization key of this operation.</summary>
    public abstract string Name { get; }

    /// <summary>Gets whether this operation forms a global execution barrier.</summary>
    public virtual AegiTaskMode Mode => AegiTaskMode.Parallel;

    /// <summary>Gets whether cancellation may be requested before commit.</summary>
    public virtual bool CanCancel => true;

    /// <summary>Gets the editing scopes affected when an editing lease is acquired.</summary>
    public virtual AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.None;

    /// <summary>Gets whether the declared editing lease covers the entire execution.</summary>
    public virtual bool RestrictEditingDuringExecution => EditRestriction != AegiTaskEditRestriction.None;

    /// <summary>Gets the owning project scope, or null for application operations.</summary>
    public virtual string? ScopeId => null;

    /// <summary>Gets optional display context for this task.</summary>
    public virtual string? ScopeDisplayName => null;

    /// <summary>Gets all exclusive resources needed by this operation.</summary>
    public virtual IReadOnlyCollection<AegiTaskResource> Resources => Array.Empty<AegiTaskResource>();

    /// <summary>Gets the target key for replaceable pending continuous writes.</summary>
    public virtual string? CoalescingKey => null;

    /// <summary>Executes preparation, commit and cleanup on the submitting synchronization context.</summary>
    protected abstract Task ExecuteAsync(AegiTaskExecutionContext context);

    internal virtual async Task<object?> ExecuteCoreAsync(AegiTaskExecutionContext context)
    {
        await ExecuteAsync(context);
        return null;
    }

    internal virtual bool HasResult => false;
}
