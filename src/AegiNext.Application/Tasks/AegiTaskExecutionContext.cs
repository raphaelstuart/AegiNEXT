namespace AegiNext.Application.Tasks;

/// <summary>Provides progress, cancellation and internal execution phases for one task.</summary>
public sealed class AegiTaskExecutionContext
{
    private static readonly AsyncLocal<AegiTaskExecutionContext?> current = new();
    private readonly object stageGate = new();
    private readonly List<Task> stages = new();
    private bool stagesDrained;
    private readonly AegiTaskService service;
    private readonly Guid taskId;
    private readonly IReadOnlySet<AegiTaskResource> resources;

    internal AegiTaskExecutionContext(AegiTaskService service, Guid taskId, IReadOnlySet<AegiTaskResource> resources,
        CancellationToken cancellationToken)
    {
        this.service = service;
        this.taskId = taskId;
        this.resources = resources;
        CancellationToken = cancellationToken;
    }

    /// <summary>Gets the context flowing through the current operation and its phases.</summary>
    public static AegiTaskExecutionContext? Current => current.Value;

    /// <summary>Gets the operation cancellation token, shared by all internal phases.</summary>
    public CancellationToken CancellationToken { get; }

    /// <summary>Reports measured or indeterminate progress; observer updates are coalesced.</summary>
    public void ReportProgress(AegiTaskProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        service.ReportProgress(taskId, progress);
    }

    /// <summary>Verifies that a nested operation uses only resources declared by its parent.</summary>
    public void RequireResources(IEnumerable<AegiTaskResource> requiredResources)
    {
        ArgumentNullException.ThrowIfNull(requiredResources);
        foreach (var resource in requiredResources)
        {
            if (!resources.Contains(resource))
            {
                throw new InvalidOperationException($"The parent task did not declare resource '{resource.Key}'.");
            }
        }
    }

    /// <summary>Executes a phase using the parent slot, token and declared resources.</summary>
    public Task RunStageAsync(string stage, Func<AegiTaskExecutionContext, Task> execute,
        IEnumerable<AegiTaskResource>? requiredResources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ValidateStage(stage, requiredResources);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TrackStage(completion.Task);
        _ = ExecuteStageAsync(execute, completion);
        return completion.Task;
    }

    /// <summary>Executes a phase with a result without reentering the global queue.</summary>
    public Task<TResult> RunStageAsync<TResult>(string stage,
        Func<AegiTaskExecutionContext, Task<TResult>> execute,
        IEnumerable<AegiTaskResource>? requiredResources = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ValidateStage(stage, requiredResources);
        var completion = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        TrackStage(completion.Task);
        _ = ExecuteStageAsync(execute, completion);
        return completion.Task;
    }

    /// <summary>Registers an independent operation to submit only after this task succeeds and releases its resources.</summary>
    public void ScheduleAfterCompletion(AegiTask task, Action<AegiTaskHandle?>? submitted = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        service.ScheduleAfterCompletion(taskId, task, submitted);
    }

    /// <summary>Acquires the editing restriction declared by the owning task.</summary>
    public AegiTaskEditLease AcquireEditLease()
    {
        return service.AcquireEditLease(taskId);
    }

    /// <summary>Validates input and begins atomic commit, after which cancellation is rejected.</summary>
    public void EnterCommit(Func<bool>? validity = null)
    {
        CancellationToken.ThrowIfCancellationRequested();
        if (validity is not null && !validity())
        {
            throw new OperationCanceledException("The prepared result is no longer valid.", CancellationToken);
        }

        service.EnterCommit(taskId);
    }

    internal async Task DrainStagesAsync()
    {
        Exception? error = null;
        var observedCount = 0;
        while (true)
        {
            Task[] pending;
            lock (stageGate)
            {
                if (stages.Count == observedCount)
                {
                    stagesDrained = true;
                    break;
                }

                pending = stages.Skip(observedCount).ToArray();
                observedCount = stages.Count;
            }

            try
            {
                await Task.WhenAll(pending);
            }
            catch (Exception exception)
            {
                error ??= exception;
            }
        }

        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private void ValidateStage(string stage, IEnumerable<AegiTaskResource>? requiredResources)
    {
        if (requiredResources is not null)
        {
            RequireResources(requiredResources);
        }

        CancellationToken.ThrowIfCancellationRequested();
        service.SetStage(taskId, stage);
    }

    private void TrackStage(Task stage)
    {
        lock (stageGate)
        {
            if (stagesDrained)
            {
                throw new InvalidOperationException("The parent operation has finished its internal phases.");
            }

            stages.Add(stage);
        }
    }

    private async Task ExecuteStageAsync(Func<AegiTaskExecutionContext, Task> execute, TaskCompletionSource completion)
    {
        try
        {
            await execute(this);
            completion.TrySetResult();
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    private async Task ExecuteStageAsync<TResult>(Func<AegiTaskExecutionContext, Task<TResult>> execute,
        TaskCompletionSource<TResult> completion)
    {
        try
        {
            completion.TrySetResult(await execute(this));
        }
        catch (OperationCanceledException exception)
        {
            completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }
    }

    internal bool BelongsTo(AegiTaskService candidate) => ReferenceEquals(service, candidate);

    internal static AegiTaskExecutionContext? SetCurrent(AegiTaskExecutionContext? value)
    {
        var previous = current.Value;
        current.Value = value;
        return previous;
    }
}
