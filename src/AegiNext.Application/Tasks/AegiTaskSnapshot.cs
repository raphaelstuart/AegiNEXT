namespace AegiNext.Application.Tasks;

/// <summary>A lightweight immutable task status, safe to retain after execution ends.</summary>
public sealed record AegiTaskSnapshot(
    Guid Id,
    long SubmissionSequence,
    string Name,
    string? ScopeId,
    string? ScopeDisplayName,
    AegiTaskMode Mode,
    AegiTaskState State,
    bool CanCancel,
    AegiTaskProgress Progress,
    DateTimeOffset SubmittedAt,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? FinishedAt = null,
    string? ErrorSummary = null)
{
    /// <summary>Gets whether execution and cleanup have ended.</summary>
    public bool IsFinished => State is AegiTaskState.Succeeded or AegiTaskState.Cancelled or AegiTaskState.Failed;
}
