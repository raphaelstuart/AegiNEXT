using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Settings.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed class ProjectPersistenceCoordinator : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly TimeProvider timeProvider;
    private readonly Func<Action, Task> dispatch;
    private readonly Func<ProjectPersistenceState?> capture;
    private readonly Action<ProjectPersistenceSaveResult> saved;
    private readonly Action<Exception> reportError;
    private readonly IProjectPersistenceStorage storage;
    private readonly CancellationTokenSource lifetime = new();
    private ProjectPreferences preferences = new();
    private ITimer? autoSaveTimer;
    private ITimer? backupTimer;
    private readonly AegiTaskService tasks;
    private readonly string scopeId;
    private readonly bool ownsTasks;
    private readonly HashSet<Task> pendingOperations = [];
    private IReadOnlyCollection<AegiTaskResource> resources = [];
    private CancellationToken OperationToken => AegiTaskExecutionContext.Current?.CancellationToken ?? lifetime.Token;
    private Task? disposal;
    private string? autoSaveFingerprint;
    private string? backupFingerprint;
    private long projectVersion;
    private long scheduleVersion;
    private int pauseCount;
    private bool active;
    private bool disposed;
    private bool prunePending;
    private bool pruneRequired;

    internal ProjectPersistenceCoordinator(TimeProvider timeProvider, Func<Action, Task> dispatch,
        Func<ProjectPersistenceState?> capture, Action<ProjectPersistenceSaveResult> saved,
        Action<Exception> reportError, IProjectPersistenceStorage? storage = null, AegiTaskService? tasks = null, string? scopeId = null)
    {
        this.timeProvider = timeProvider;
        this.dispatch = dispatch;
        this.capture = capture;
        this.saved = saved;
        this.reportError = reportError;
        this.storage = storage ?? new ProjectPersistenceStorage();
        ownsTasks = tasks is null;
        this.tasks = tasks ?? new();
        this.scopeId = scopeId ?? Guid.NewGuid().ToString("N");
        if (ownsTasks)
        {
            this.tasks.RegisterScope(this.scopeId, "Tasks.ProjectPersistence");
        }
        resources = [AegiTaskResource.Project(this.scopeId)];
    }

    internal Task Completion => DrainAsync();

    internal void UpdatePreferences(ProjectPreferences value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (preferences == value)
            {
                return;
            }

            pruneRequired |= value.MaximumBackupCount < preferences.MaximumBackupCount;
            preferences = value;
            RestartSchedulingLocked();
            QueuePruneLocked();
        }
    }

    internal void ActivateProject()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            projectVersion++;
            var state = capture();
            resources = state is null ? [AegiTaskResource.Project(scopeId)] :
                [AegiTaskResource.Project(scopeId), AegiTaskResource.StoragePath(state.ProjectPath)];
            active = true;
            autoSaveFingerprint = null;
            backupFingerprint = null;
            RestartSchedulingLocked();
            QueuePruneLocked();
        }
    }

    internal async Task<ProjectPersistencePauseLease> PauseAsync()
    {
        Task pending;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            pauseCount++;
            StopSchedulingLocked();
            pending = DrainAsync();
        }

        if (AegiTaskExecutionContext.Current is null)
        {
            await pending.ConfigureAwait(false);
        }
        return new(Resume);
    }

    internal Task<T> RunExclusiveAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return EnqueueLocked(async () =>
            {
                var task = await InvokeAsync(operation).ConfigureAwait(false);
                return await task.ConfigureAwait(false);
            });
        }
    }

    internal async Task RecordManualSaveAsync(ProjectDocument persistedSnapshot)
    {
        ArgumentNullException.ThrowIfNull(persistedSnapshot);
        long version;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            version = projectVersion;
        }

        var fingerprint = await Task.Run(() => ComputeFingerprint(persistedSnapshot)).ConfigureAwait(false);
        lock (gate)
        {
            if (!disposed && version == projectVersion)
            {
                autoSaveFingerprint = fingerprint;
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (disposal is null)
            {
                disposed = true;
                StopSchedulingLocked();
                lifetime.Cancel();
                foreach (var handle in tasks.GetSnapshots().Where(value => value.ScopeId == scopeId && !value.IsFinished &&
                    value.Name.StartsWith("Tasks.Project", StringComparison.Ordinal)))
                {
                    tasks.RequestCancel(handle.Id);
                }
                disposal = DisposeCoreAsync(DrainAsync());
            }

            return new(disposal);
        }
    }

    private async Task DisposeCoreAsync(Task pending)
    {
        await pending.ConfigureAwait(false);
        lifetime.Dispose();
        if (ownsTasks)
        {
            await tasks.DisposeAsync().ConfigureAwait(false);
        }
    }

    private void Resume()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            pauseCount--;
            if (pauseCount == 0)
            {
                RestartSchedulingLocked();
                QueuePruneLocked();
            }
        }
    }

    private void RestartSchedulingLocked()
    {
        StopSchedulingLocked();
        if (!active || pauseCount != 0 || disposed)
        {
            return;
        }

        var revision = scheduleVersion;
        using var executionFlow = ExecutionContext.SuppressFlow();
        if (preferences.AutoSaveEnabled)
        {
            var interval = TimeSpan.FromMinutes(preferences.AutoSaveIntervalMinutes);
            autoSaveTimer = timeProvider.CreateTimer(_ => QueueTick(true, revision), null, interval, interval);
        }
        if (preferences.BackupEnabled)
        {
            var interval = TimeSpan.FromMinutes(preferences.BackupIntervalMinutes);
            backupTimer = timeProvider.CreateTimer(_ => QueueTick(false, revision), null, interval, interval);
        }
    }

    private void StopSchedulingLocked()
    {
        scheduleVersion++;
        autoSaveTimer?.Dispose();
        backupTimer?.Dispose();
        autoSaveTimer = null;
        backupTimer = null;
    }

    private void QueueTick(bool autoSave, long revision)
    {
        lock (gate)
        {
            if (!CanStart(projectVersion, revision))
            {
                return;
            }
            TrackPendingLocked(CaptureAndSubmitTickAsync(autoSave, projectVersion, revision));
        }
    }

    private async Task CaptureAndSubmitTickAsync(bool autoSave, long version, long revision)
    {
        try
        {
            var state = await InvokeAsync(capture).ConfigureAwait(false);
            if (state is null || state.IsBusy || ProjectBackupStore.IsBackupPath(state.ProjectPath))
            {
                return;
            }
            Task pending;
            lock (gate)
            {
                if (!CanStart(version, revision))
                {
                    return;
                }
                pending = tasks.Submit(new AutomaticProjectPersistenceTask(this, scopeId, state,
                    autoSave, version, revision)).Completion;
            }
            await pending.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (CanStart(version, revision))
            {
                await ReportAsync(error).ConfigureAwait(false);
            }
        }
    }

    internal async Task RunTickAsync(ProjectPersistenceState state, bool autoSave, long version, long revision)
    {
        if (!CanStart(version, revision) || !await InvokeAsync(() => IsCurrentState(state, version)).ConfigureAwait(false))
        {
            return;
        }

        var normalized = ProjectResources.NormalizeMediaReferences(state.Snapshot, state.ProjectDirectory);
        var fingerprint = ComputeFingerprint(normalized);
        if (autoSave)
        {
            bool unchanged;
            lock (gate)
            {
                unchanged = fingerprint == autoSaveFingerprint;
                if (!state.HasUnsavedChanges)
                {
                    autoSaveFingerprint = fingerprint;
                    return;
                }
            }

            if (!unchanged)
            {
                await storage.SaveAsync(state.Snapshot, state.ProjectPath, OperationToken).ConfigureAwait(false);
            }
            await InvokeAsync(() =>
            {
                if (IsCurrentState(state, version))
                {
                    saved(new(state, state.Snapshot));
                    lock (gate)
                    {
                        autoSaveFingerprint = fingerprint;
                    }
                }
                return true;
            }).ConfigureAwait(false);
        }
        else
        {
            bool unchanged;
            int maximumCount;
            bool needsPrune;
            lock (gate)
            {
                unchanged = fingerprint == backupFingerprint;
                maximumCount = preferences.MaximumBackupCount;
                needsPrune = pruneRequired;
            }

            if (unchanged)
            {
                if (needsPrune)
                {
                    await PruneAsync(state, version, maximumCount).ConfigureAwait(false);
                }
                return;
            }

            try
            {
                await storage.WriteBackupAsync(state.Snapshot, state.ProjectPath, timeProvider.GetUtcNow(),
                    maximumCount, OperationToken).ConfigureAwait(false);
            }
            catch (ProjectBackupPruneException)
            {
                lock (gate)
                {
                    if (version == projectVersion)
                    {
                        backupFingerprint = fingerprint;
                        pruneRequired = true;
                    }
                }
                throw;
            }

            lock (gate)
            {
                if (version == projectVersion)
                {
                    backupFingerprint = fingerprint;
                    pruneRequired = preferences.MaximumBackupCount < maximumCount;
                }
            }
        }
    }

    private bool CanStart(long version, long revision)
    {
        lock (gate)
        {
            return !disposed && active && pauseCount == 0 && version == projectVersion && revision == scheduleVersion;
        }
    }

    private bool IsCurrentState(ProjectPersistenceState state, long version)
    {
        lock (gate)
        {
            if (disposed || version != projectVersion)
            {
                return false;
            }
        }

        var current = capture();
        return current is not null && current.Generation == state.Generation &&
            string.Equals(current.ProjectPath, state.ProjectPath,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

    private void QueuePruneLocked()
    {
        if (!pruneRequired || prunePending || disposed || !active || pauseCount != 0)
        {
            return;
        }

        prunePending = true;
        var version = projectVersion;
        _ = EnqueueLocked(async () =>
        {
            var repeat = false;
            try
            {
                var state = await InvokeAsync(capture).ConfigureAwait(false);
                int maximumCount;
                lock (gate)
                {
                    if (disposed || pauseCount != 0 || version != projectVersion)
                    {
                        return false;
                    }
                    maximumCount = preferences.MaximumBackupCount;
                }
                if (state is not null && !state.IsBusy && !ProjectBackupStore.IsBackupPath(state.ProjectPath))
                {
                    await PruneAsync(state, version, maximumCount).ConfigureAwait(false);
                    lock (gate)
                    {
                        repeat = version == projectVersion && pruneRequired;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception error)
            {
                await ReportAsync(error).ConfigureAwait(false);
                throw;
            }
            finally
            {
                lock (gate)
                {
                    prunePending = false;
                    if (repeat)
                    {
                        QueuePruneLocked();
                    }
                }
            }
            return true;
        }, "Tasks.ProjectBackupPrune");
    }

    private async Task PruneAsync(ProjectPersistenceState state, long version, int maximumCount)
    {
        await storage.PruneBackupsAsync(state.ProjectPath, maximumCount, OperationToken).ConfigureAwait(false);
        lock (gate)
        {
            if (version == projectVersion)
            {
                pruneRequired = preferences.MaximumBackupCount < maximumCount;
            }
        }
    }

    private Task<bool> ReportAsync(Exception error)
    {
        return InvokeAsync(() =>
        {
            lock (gate)
            {
                if (disposed)
                {
                    return false;
                }
            }
            reportError(error);
            return true;
        });
    }

    private async Task<T> InvokeAsync<T>(Func<T> action)
    {
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        await dispatch(() =>
        {
            try
            {
                result.TrySetResult(action());
            }
            catch (Exception error)
            {
                result.TrySetException(error);
            }
        }).ConfigureAwait(false);
        return await result.Task.ConfigureAwait(false);
    }

    private Task<T> EnqueueLocked<T>(Func<Task<T>> operation, string name = "Tasks.ProjectPersistence")
    {
        var pending = AegiTaskExecutionContext.Current is { } parent
            ? parent.RunStageAsync(name, _ => operation(), resources)
            : tasks.Submit(new ProjectPersistenceTask<T>(scopeId, resources, name, operation)).Completion;
        TrackPendingLocked(pending);
        return pending;
    }

    private void TrackPendingLocked(Task pending)
    {
        pendingOperations.Add(pending);
        _ = RemoveCompletedAsync(pending);
    }

    private async Task RemoveCompletedAsync(Task pending)
    {
        await ObserveAsync(pending).ConfigureAwait(false);
        lock (gate)
        {
            pendingOperations.Remove(pending);
        }
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (gate)
            {
                pending = pendingOperations.ToArray();
            }
            if (pending.Length == 0)
            {
                return;
            }
            await ObserveAsync(Task.WhenAll(pending)).ConfigureAwait(false);
        }
    }

    private static async Task ObserveAsync(Task pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private static string ComputeFingerprint(ProjectDocument snapshot)
    {
        return ProjectStore.ComputeFingerprint(snapshot);
    }
}
