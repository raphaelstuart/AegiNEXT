namespace AegiNext.Application.Tasks;

/// <summary>Owns a strict submission queue, global barriers and exclusive resource leases.</summary>
public sealed class AegiTaskService : IAegiTaskService
{
    private const int HISTORY_LIMIT = 50;
    private readonly object gate = new();
    private readonly LinkedList<AegiTaskEntry> queue = new();
    private readonly Dictionary<Guid, AegiTaskEntry> entries = new();
    private readonly Dictionary<string, AegiTaskScopeState> scopes = new(StringComparer.Ordinal);
    private readonly HashSet<AegiTaskResource> occupiedResources = new();
    private readonly LinkedList<AegiTaskSnapshot> history = new();
    private readonly Timer progressTimer;
    private long submissionSequence;
    private int maximumConcurrentTasks = 4;
    private int runningCount;
    private int globalEditLeases;
    private bool blockingRunning;
    private bool accepting = true;
    private bool progressDirty;
    private bool pumping;
    private Task lastDispatchStarted = Task.CompletedTask;

    /// <summary>Creates the service with the default four execution slots.</summary>
    public AegiTaskService()
    {
        progressTimer = new(_ => FlushProgress(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public int MaximumConcurrentTasks
    {
        get
        {
            lock (gate)
            {
                return maximumConcurrentTasks;
            }
        }
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 32);
            lock (gate)
            {
                maximumConcurrentTasks = value;
            }

            Pump();
            NotifyChanged();
        }
    }

    /// <inheritdoc />
    public void RegisterScope(string scopeId, string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        ArgumentNullException.ThrowIfNull(displayName);
        lock (gate)
        {
            if (scopes.TryGetValue(scopeId, out var scope))
            {
                scope.DisplayName = displayName;
            }
            else
            {
                scopes.Add(scopeId, new(displayName));
            }
        }

        NotifyChanged();
    }

    /// <inheritdoc />
    public AegiTaskHandle Submit(AegiTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return SubmitCore(task, null, null, static (completion, snapshot, cancel) => new(completion, snapshot, cancel));
    }

    /// <inheritdoc />
    public AegiTaskHandle<TResult> Submit<TResult>(AegiTask<TResult> task)
    {
        ArgumentNullException.ThrowIfNull(task);
        return (AegiTaskHandle<TResult>)SubmitCore(task, null, null,
            static (completion, snapshot, cancel) => new AegiTaskHandle<TResult>(completion, snapshot, cancel));
    }

    /// <inheritdoc />
    public IReadOnlyList<AegiTaskSnapshot> GetSnapshots()
    {
        lock (gate)
        {
            return entries.Values.OrderBy(entry => entry.Snapshot.SubmissionSequence).Select(entry => entry.Snapshot)
                .Concat(history).ToArray();
        }
    }

    /// <inheritdoc />
    public bool IsEditingRestricted(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        lock (gate)
        {
            return globalEditLeases > 0 || (scopes.TryGetValue(scopeId, out var scope) && scope.EditLeases > 0);
        }
    }

    /// <summary>Acquires a scope lease for synchronous UI interactions outside a scheduled operation.</summary>
    public AegiTaskEditLease AcquireScopeEditLease(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        lock (gate)
        {
            var scope = GetScope(scopeId);
            if (!scope.Accepting)
            {
                throw new InvalidOperationException("The owning project no longer accepts editing interactions.");
            }

            scope.EditLeases++;
        }

        NotifyChanged();
        return new(() =>
        {
            lock (gate)
            {
                GetScope(scopeId).EditLeases--;
            }

            NotifyChanged();
        });
    }

    /// <summary>Waits for currently submitted scope operations without sealing future submissions.</summary>
    public Task DrainScopeAsync(string scopeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        Task[] completions;
        lock (gate)
        {
            GetScope(scopeId);
            completions = entries.Values.Where(entry => entry.Snapshot.ScopeId == scopeId)
                .Select(entry => ObserveCompletionAsync(entry.Completion.Task)).ToArray();
        }

        return Task.WhenAll(completions).WaitAsync(cancellationToken);
    }

    /// <inheritdoc />
    public AegiTaskScopeCloseLease BeginCloseScope(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        Guid[] cancellable;
        AegiTaskEntry[] cancelledQueued;
        long generation;
        lock (gate)
        {
            var scope = GetScope(scopeId);
            if (!scope.Accepting || scope.Closing)
            {
                throw new InvalidOperationException("The project scope is already closing or closed.");
            }

            scope.Accepting = false;
            scope.Closing = true;
            generation = scope.Generation;
            cancelledQueued = entries.Values.Where(entry => entry.Snapshot.ScopeId == scopeId
                && entry.Snapshot.State == AegiTaskState.Queued && entry.Snapshot.CanCancel).ToArray();
            foreach (var entry in cancelledQueued)
            {
                queue.Remove(entry);
                FinishUnderLock(entry, AegiTaskState.Cancelled, null);
            }

            cancellable = entries.Values.Where(entry => entry.Snapshot.ScopeId == scopeId && entry.Snapshot.CanCancel)
                .Select(entry => entry.Snapshot.Id).ToArray();
        }

        foreach (var entry in cancelledQueued)
        {
            entry.Completion.TrySetCanceled();
            ReleaseEntryReferences(entry);
        }

        foreach (var id in cancellable)
        {
            RequestCancel(id);
        }

        Pump();
        NotifyChanged();
        return new(this, scopeId, generation);
    }

    /// <summary>Explicitly requests cancellation by task identity without retaining its execution handle.</summary>
    public bool RequestCancel(Guid taskId)
    {
        AegiTaskEntry? queuedCancellation = null;
        AegiTaskEntry? runningCancellation = null;
        lock (gate)
        {
            if (!entries.TryGetValue(taskId, out var entry) || !entry.Snapshot.CanCancel)
            {
                return false;
            }

            if (entry.Snapshot.State == AegiTaskState.Queued)
            {
                queue.Remove(entry);
                FinishUnderLock(entry, AegiTaskState.Cancelled, null);
                queuedCancellation = entry;
            }
            else
            {
                entry.CancellationCallbacks = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Update(entry, entry.Snapshot with { State = AegiTaskState.Cancelling, CanCancel = false });
                runningCancellation = entry;
            }
        }

        if (queuedCancellation is not null)
        {
            queuedCancellation.Completion.TrySetCanceled();
            ReleaseEntryReferences(queuedCancellation);
        }

        if (runningCancellation is not null)
        {
            _ = CancelCallbacksAsync(runningCancellation);
        }

        NotifyChanged();
        Pump();
        return true;
    }

    /// <inheritdoc />
    public void ClearHistory()
    {
        lock (gate)
        {
            history.Clear();
        }

        NotifyChanged();
    }

    /// <inheritdoc />
    public Task DrainAsync(CancellationToken cancellationToken = default)
    {
        Task[] completions;
        lock (gate)
        {
            completions = entries.Values.Select(entry => ObserveCompletionAsync(entry.Completion.Task)).ToArray();
        }

        return Task.WhenAll(completions).WaitAsync(cancellationToken);
    }

    /// <summary>Stops acceptance and drains all operations before disposing service infrastructure.</summary>
    public async ValueTask DisposeAsync()
    {
        lock (gate)
        {
            accepting = false;
        }

        await DrainAsync().ConfigureAwait(false);
        await progressTimer.DisposeAsync().ConfigureAwait(false);
    }

    internal AegiTaskHandle SubmitFinalization(AegiTask task, string scopeId, long generation)
    {
        return SubmitCore(task, scopeId, generation, static (completion, snapshot, cancel) => new(completion, snapshot, cancel));
    }

    internal AegiTaskHandle<TResult> SubmitFinalization<TResult>(AegiTask<TResult> task, string scopeId, long generation)
    {
        return (AegiTaskHandle<TResult>)SubmitCore(task, scopeId, generation,
            static (completion, snapshot, cancel) => new AegiTaskHandle<TResult>(completion, snapshot, cancel));
    }

    internal Task DrainScopeAsync(string scopeId, long generation, CancellationToken cancellationToken)
    {
        Task[] completions;
        lock (gate)
        {
            ValidateClosingScope(scopeId, generation);
            completions = entries.Values.Where(entry => entry.Snapshot.ScopeId == scopeId)
                .Select(entry => ObserveCompletionAsync(entry.Completion.Task)).ToArray();
        }

        return Task.WhenAll(completions).WaitAsync(cancellationToken);
    }

    internal void CompleteScopeClose(string scopeId, long generation)
    {
        lock (gate)
        {
            var scope = ValidateClosingScope(scopeId, generation);
            if (entries.Values.Any(entry => entry.Snapshot.ScopeId == scopeId))
            {
                throw new InvalidOperationException("Scope close requires all task cleanup and final persistence to finish.");
            }

            scope.Closing = false;
            scope.Generation++;
        }

        NotifyChanged();
    }

    internal void ReopenScope(string scopeId, long generation)
    {
        lock (gate)
        {
            if (!scopes.TryGetValue(scopeId, out var scope) || !scope.Closing || scope.Generation != generation)
            {
                return;
            }

            scope.Accepting = true;
            scope.Closing = false;
            scope.Generation++;
        }

        NotifyChanged();
    }

    internal void ScheduleAfterCompletion(Guid taskId, AegiTask task, Action<AegiTaskHandle?>? submitted)
    {
        var definition = AegiTaskDefinition.Capture(task);
        if (definition.HasResult)
        {
            throw new ArgumentException("A follow-up without an observable result must be a non-result operation.", nameof(task));
        }

        lock (gate)
        {
            var entry = GetRunningEntry(taskId);
            if (definition.ScopeId is { } scopeId)
            {
                GetScope(scopeId);
            }

            entry.FollowUps.Add(new(task, submitted));
        }
    }

    internal AegiTaskEditLease AcquireEditLease(Guid taskId)
    {
        lock (gate)
        {
            var entry = GetRunningEntry(taskId);
            entry.Cancellation.Token.ThrowIfCancellationRequested();
            AcquireEditingUnderLock(entry);
        }

        NotifyChanged();
        return new(() => ReleaseEditLease(taskId));
    }

    internal void EnterCommit(Guid taskId)
    {
        lock (gate)
        {
            var entry = GetRunningEntry(taskId);
            if (entry.Snapshot.State == AegiTaskState.Cancelling)
            {
                throw new OperationCanceledException(entry.Cancellation.Token);
            }

            entry.Cancellation.Token.ThrowIfCancellationRequested();
            Update(entry, entry.Snapshot with { State = AegiTaskState.Committing, CanCancel = false });
        }

        NotifyChanged();
    }

    internal void ReportProgress(Guid taskId, AegiTaskProgress progress)
    {
        lock (gate)
        {
            if (entries.TryGetValue(taskId, out var entry))
            {
                Update(entry, entry.Snapshot with { Progress = progress });
                progressDirty = true;
            }
        }
    }

    internal void SetStage(Guid taskId, string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        lock (gate)
        {
            var entry = GetRunningEntry(taskId);
            Update(entry, entry.Snapshot with { Progress = new(stage) });
        }

        NotifyChanged();
    }

    private AegiTaskHandle SubmitCore(AegiTask task, string? finalizationScope, long? generation,
        Func<Task<object?>, AegiTaskSnapshot, Func<bool>, AegiTaskHandle> createHandle,
        SynchronizationContext? submissionContext = null)
    {
        if (AegiTaskExecutionContext.Current is { } current && current.BelongsTo(this))
        {
            throw new InvalidOperationException("Nested operations must use the parent execution context and declared resources.");
        }

        var definition = AegiTaskDefinition.Capture(task);
        var resources = definition.Resources;
        AegiTaskHandle handle;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(!accepting, this);
            var scopeGeneration = 0L;
            string? displayName = definition.ScopeDisplayName;
            if (definition.ScopeId is { } scopeId)
            {
                var scope = GetScope(scopeId);
                if (scopeId == finalizationScope && generation is { } expectedGeneration)
                {
                    ValidateClosingScope(scopeId, expectedGeneration);
                }
                else if (!scope.Accepting)
                {
                    throw new InvalidOperationException("The owning project no longer accepts tasks.");
                }

                scopeGeneration = scope.Generation;
                displayName ??= scope.DisplayName;
            }

            var coalesced = FindCoalescingCandidate(definition, resources, scopeGeneration);
            if (coalesced is not null)
            {
                coalesced.Task = task;
                coalesced.SynchronizationContext = submissionContext ?? SynchronizationContext.Current;
                Update(coalesced, coalesced.Snapshot with { Name = definition.Name, ScopeDisplayName = displayName });
                handle = coalesced.Handle;
            }
            else
            {
                var id = Guid.NewGuid();
                var snapshot = new AegiTaskSnapshot(id, ++submissionSequence, definition.Name, definition.ScopeId,
                    displayName, definition.Mode, AegiTaskState.Queued, definition.CanCancel, new(), DateTimeOffset.UtcNow);
                var entry = new AegiTaskEntry(task, definition, snapshot, submissionContext ?? SynchronizationContext.Current, scopeGeneration);
                handle = createHandle(entry.Completion.Task, snapshot, () => RequestCancel(id));
                entry.Handle = handle;
                entries.Add(id, entry);
                queue.AddLast(entry);
            }
        }

        NotifyChanged();
        Pump();
        return handle;
    }

    private AegiTaskEntry? FindCoalescingCandidate(AegiTaskDefinition definition, HashSet<AegiTaskResource> resources, long scopeGeneration)
    {
        if (definition.HasResult || definition.CoalescingKey is null || definition.Mode == AegiTaskMode.Blocking)
        {
            return null;
        }

        for (var node = queue.Last; node is not null; node = node.Previous)
        {
            var entry = node.Value;
            if (entry.Snapshot.Mode == AegiTaskMode.Blocking)
            {
                break;
            }

            var sameGroup = entry.TaskType == definition.TaskType && entry.CoalescingKey == definition.CoalescingKey
                && entry.Snapshot.ScopeId == definition.ScopeId && entry.ScopeGeneration == scopeGeneration
                && entry.Snapshot.Mode == definition.Mode && entry.Snapshot.CanCancel == definition.CanCancel
                && entry.EditRestriction == definition.EditRestriction
                && entry.RestrictEditingDuringExecution == definition.RestrictEditingDuringExecution
                && entry.Resources.SetEquals(resources);
            if (sameGroup)
            {
                return entry;
            }

            if (entry.Resources.Overlaps(resources))
            {
                break;
            }
        }

        return null;
    }

    private void Pump()
    {
        lock (gate)
        {
            if (pumping)
            {
                return;
            }

            pumping = true;
        }

        while (true)
        {
            AegiTaskEntry entry;
            lock (gate)
            {
                if (blockingRunning || runningCount >= maximumConcurrentTasks || queue.First is not { } first)
                {
                    pumping = false;
                    return;
                }

                entry = first.Value;
                if ((entry.Snapshot.Mode == AegiTaskMode.Blocking && runningCount != 0)
                    || occupiedResources.Overlaps(entry.Resources))
                {
                    pumping = false;
                    return;
                }

                queue.RemoveFirst();
                runningCount++;
                blockingRunning = entry.Snapshot.Mode == AegiTaskMode.Blocking;
                occupiedResources.UnionWith(entry.Resources);
                entry.OwnsResources = true;
                entry.StartPrerequisite = lastDispatchStarted;
                lastDispatchStarted = entry.DispatchStarted.Task;
                Update(entry, entry.Snapshot with { State = AegiTaskState.Running, StartedAt = DateTimeOffset.UtcNow });
                if (entry.RestrictEditingDuringExecution)
                {
                    AcquireEditingUnderLock(entry);
                }
            }

            Start(entry);
            NotifyChanged();
        }
    }

    private void Start(AegiTaskEntry entry)
    {
        try
        {
            if (entry.SynchronizationContext is { } context)
            {
                context.Post(_ => _ = RunEntryAsync(entry), null);
            }
            else
            {
                ThreadPool.QueueUserWorkItem(_ => _ = RunEntryAsync(entry));
            }
        }
        catch (Exception exception)
        {
            entry.DispatchStarted.TrySetResult();
            _ = CompleteEntryAsync(entry, null, exception);
        }
    }

    private async Task RunEntryAsync(AegiTaskEntry entry)
    {
        var executionContext = new AegiTaskExecutionContext(this, entry.Snapshot.Id, entry.Resources, entry.Cancellation.Token);
        var previous = AegiTaskExecutionContext.SetCurrent(executionContext);
        object? result = null;
        Exception? error = null;
        try
        {
            await entry.StartPrerequisite;
            entry.Cancellation.Token.ThrowIfCancellationRequested();
            Task<object?> operation;
            try
            {
                operation = entry.Task!.ExecuteCoreAsync(executionContext);
            }
            finally
            {
                entry.DispatchStarted.TrySetResult();
            }

            result = await operation;
        }
        catch (Exception exception)
        {
            error = exception;
        }
        finally
        {
            entry.DispatchStarted.TrySetResult();
            try
            {
                await executionContext.DrainStagesAsync();
            }
            catch (Exception exception)
            {
                error ??= exception;
            }

            AegiTaskExecutionContext.SetCurrent(previous);
        }

        await CompleteEntryAsync(entry, result, error).ConfigureAwait(false);
    }

    private async Task CompleteEntryAsync(AegiTaskEntry entry, object? result, Exception? error)
    {
        Task? cancellationCallbacks;
        lock (gate)
        {
            Update(entry, entry.Snapshot with { CanCancel = false });
            cancellationCallbacks = entry.CancellationCallbacks?.Task;
        }

        if (cancellationCallbacks is not null)
        {
            try
            {
                await cancellationCallbacks.ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                error = exception;
            }
        }

        var errorSummary = error is null ? null : GetErrorSummary(error);
        AegiTaskState state;
        lock (gate)
        {
            state = error is OperationCanceledException || (error is null && entry.Snapshot.State == AegiTaskState.Cancelling)
                ? AegiTaskState.Cancelled
                : error is null ? AegiTaskState.Succeeded : AegiTaskState.Failed;
            FinishUnderLock(entry, state, errorSummary);
        }

        if (state == AegiTaskState.Succeeded)
        {
            SubmitFollowUps(entry);
        }
        else
        {
            AbandonFollowUps(entry);
        }

        if (state == AegiTaskState.Cancelled)
        {
            entry.Completion.TrySetCanceled();
        }
        else if (error is not null)
        {
            entry.Completion.TrySetException(error);
        }
        else
        {
            entry.Completion.TrySetResult(result);
        }

        ReleaseEntryReferences(entry);
        NotifyChanged();
        Pump();
    }

    private void SubmitFollowUps(AegiTaskEntry entry)
    {
        var previous = AegiTaskExecutionContext.SetCurrent(null);
        try
        {
            foreach (var followUp in entry.FollowUps)
            {
                var task = followUp.Task;
                var taskScopeId = task.ScopeId;
                var canSubmit = false;
                lock (gate)
                {
                    canSubmit = accepting && (taskScopeId is not { } scopeId || GetScope(scopeId).Accepting);
                }

                AegiTaskHandle? handle = null;
                if (canSubmit)
                {
                    try
                    {
                        handle = SubmitCore(task, null, null, static (completion, snapshot, cancel) => new(completion, snapshot, cancel),
                            entry.SynchronizationContext);
                    }
                    catch (InvalidOperationException)
                    {
                        // Closing may stop acceptance between the check and submission.
                    }
                }

                NotifyFollowUp(entry.SynchronizationContext, followUp.Submitted, handle);
            }
        }
        finally
        {
            AegiTaskExecutionContext.SetCurrent(previous);
            entry.FollowUps.Clear();
        }
    }

    private static void AbandonFollowUps(AegiTaskEntry entry)
    {
        foreach (var followUp in entry.FollowUps)
        {
            NotifyFollowUp(entry.SynchronizationContext, followUp.Submitted, null);
        }

        entry.FollowUps.Clear();
    }

    private static void NotifyFollowUp(SynchronizationContext? synchronizationContext, Action<AegiTaskHandle?>? submitted,
        AegiTaskHandle? handle)
    {
        if (submitted is null)
        {
            return;
        }

        void Notify()
        {
            try
            {
                submitted(handle);
            }
            catch (Exception)
            {
                // Completion observers cannot interfere with the parent resource release.
            }
        }

        if (synchronizationContext is not null && !ReferenceEquals(synchronizationContext, SynchronizationContext.Current))
        {
            try
            {
                synchronizationContext.Post(_ => Notify(), null);
            }
            catch (Exception)
            {
                Notify();
            }
        }
        else
        {
            Notify();
        }
    }

    private void FinishUnderLock(AegiTaskEntry entry, AegiTaskState state, string? errorSummary)
    {
        if (entry.OwnsResources)
        {
            occupiedResources.ExceptWith(entry.Resources);
            runningCount--;
            if (entry.Snapshot.Mode == AegiTaskMode.Blocking)
            {
                blockingRunning = false;
            }

            entry.OwnsResources = false;
        }

        ReleaseAllEditingUnderLock(entry);
        Update(entry, entry.Snapshot with
        {
            State = state,
            CanCancel = false,
            FinishedAt = DateTimeOffset.UtcNow,
            ErrorSummary = state == AegiTaskState.Cancelled ? null : errorSummary,
        });
        entries.Remove(entry.Snapshot.Id);
        history.AddFirst(entry.Snapshot);
        while (history.Count > HISTORY_LIMIT)
        {
            history.RemoveLast();
        }
    }

    private void AcquireEditingUnderLock(AegiTaskEntry entry)
    {
        if (entry.EditRestriction == AegiTaskEditRestriction.None)
        {
            return;
        }

        entry.EditLeases++;
        if (entry.EditRestriction == AegiTaskEditRestriction.AllScopes)
        {
            globalEditLeases++;
        }
        else
        {
            GetScope(entry.Snapshot.ScopeId!).EditLeases++;
        }
    }

    private void ReleaseEditLease(Guid taskId)
    {
        lock (gate)
        {
            if (!entries.TryGetValue(taskId, out var entry) || entry.EditLeases == 0)
            {
                return;
            }

            entry.EditLeases--;
            if (entry.EditRestriction == AegiTaskEditRestriction.AllScopes)
            {
                globalEditLeases--;
            }
            else
            {
                GetScope(entry.Snapshot.ScopeId!).EditLeases--;
            }
        }

        NotifyChanged();
    }

    private void ReleaseAllEditingUnderLock(AegiTaskEntry entry)
    {
        if (entry.EditLeases == 0)
        {
            return;
        }

        if (entry.EditRestriction == AegiTaskEditRestriction.AllScopes)
        {
            globalEditLeases -= entry.EditLeases;
        }
        else
        {
            GetScope(entry.Snapshot.ScopeId!).EditLeases -= entry.EditLeases;
        }

        entry.EditLeases = 0;
    }

    private AegiTaskScopeState GetScope(string scopeId)
    {
        if (!scopes.TryGetValue(scopeId, out var scope))
        {
            throw new InvalidOperationException($"Project scope '{scopeId}' is not registered.");
        }

        return scope;
    }

    private AegiTaskScopeState ValidateClosingScope(string scopeId, long generation)
    {
        var scope = GetScope(scopeId);
        if (!scope.Closing || scope.Generation != generation)
        {
            throw new InvalidOperationException("The scope shutdown lease is no longer valid.");
        }

        return scope;
    }

    private AegiTaskEntry GetRunningEntry(Guid taskId)
    {
        if (!entries.TryGetValue(taskId, out var entry) || entry.Snapshot.State == AegiTaskState.Queued)
        {
            throw new InvalidOperationException("The operation is not executing.");
        }

        return entry;
    }

    private void FlushProgress()
    {
        lock (gate)
        {
            if (!progressDirty)
            {
                return;
            }

            progressDirty = false;
        }

        NotifyChanged();
    }

    private void NotifyChanged()
    {
        var handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        var previous = AegiTaskExecutionContext.SetCurrent(null);
        try
        {
            foreach (EventHandler handler in handlers.GetInvocationList())
            {
                try
                {
                    handler(this, EventArgs.Empty);
                }
                catch (Exception)
                {
                    // Observer failures cannot disrupt resource ownership or task cleanup.
                }
            }
        }
        finally
        {
            AegiTaskExecutionContext.SetCurrent(previous);
        }
    }

    private static void Update(AegiTaskEntry entry, AegiTaskSnapshot snapshot)
    {
        entry.Snapshot = snapshot;
        entry.Handle.Update(snapshot);
    }

    private static string GetErrorSummary(Exception error)
    {
        string summary;
        try
        {
            summary = error.GetType().Name + ": " + error.Message;
        }
        catch (Exception)
        {
            summary = error.GetType().Name;
        }

        return summary.Length > 1024 ? summary[..1024] : summary;
    }

    private static async Task CancelCallbacksAsync(AegiTaskEntry entry)
    {
        try
        {
            await entry.Cancellation.CancelAsync().ConfigureAwait(false);
            entry.CancellationCallbacks!.TrySetResult();
        }
        catch (Exception exception)
        {
            entry.CancellationCallbacks!.TrySetException(exception);
        }
    }

    private static async Task ObserveCompletionAsync(Task completion)
    {
        try
        {
            await completion.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Draining tracks resource cleanup rather than business success.
        }
    }

    private static void ReleaseEntryReferences(AegiTaskEntry entry)
    {
        entry.FollowUps.Clear();
        entry.Task = null;
        entry.SynchronizationContext = null;
        entry.Cancellation.Dispose();
    }
}
