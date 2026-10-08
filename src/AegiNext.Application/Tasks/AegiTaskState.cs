namespace AegiNext.Application.Tasks;

/// <summary>The observable lifecycle of an application task.</summary>
public enum AegiTaskState
{
    Queued,
    Running,
    Cancelling,
    Committing,
    Succeeded,
    Cancelled,
    Failed,
}
