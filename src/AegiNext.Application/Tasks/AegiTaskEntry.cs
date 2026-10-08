namespace AegiNext.Application.Tasks;

internal sealed class AegiTaskEntry(AegiTask task, AegiTaskDefinition definition, AegiTaskSnapshot snapshot,
    SynchronizationContext? synchronizationContext, long scopeGeneration)
{
    internal AegiTask? Task { get; set; } = task;

    internal AegiTaskSnapshot Snapshot { get; set; } = snapshot;

    internal HashSet<AegiTaskResource> Resources { get; } = definition.Resources;

    internal SynchronizationContext? SynchronizationContext { get; set; } = synchronizationContext;

    internal long ScopeGeneration { get; } = scopeGeneration;

    internal CancellationTokenSource Cancellation { get; } = new();

    internal TaskCompletionSource<object?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal TaskCompletionSource? CancellationCallbacks { get; set; }

    internal AegiTaskHandle Handle { get; set; } = null!;

    internal List<AegiTaskFollowUp> FollowUps { get; } = new();

    internal Task StartPrerequisite { get; set; } = System.Threading.Tasks.Task.CompletedTask;

    internal TaskCompletionSource DispatchStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    internal int EditLeases { get; set; }

    internal bool OwnsResources { get; set; }

    internal string? CoalescingKey { get; } = definition.CoalescingKey;

    internal Type TaskType { get; } = definition.TaskType;

    internal AegiTaskEditRestriction EditRestriction { get; } = definition.EditRestriction;

    internal bool RestrictEditingDuringExecution { get; } = definition.RestrictEditingDuringExecution;
}
