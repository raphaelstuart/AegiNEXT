using System.Security.Cryptography;
using AegiNext.Application;
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
    private Task completion = Task.CompletedTask;
    private Task? disposal;
    private string? autoSaveFingerprint;
    private string? backupFingerprint;
    private long projectVersion;
    private long scheduleVersion;
    private int pauseCount;
    private bool active;
    private bool disposed;
    private bool autoSavePending;
    private bool backupPending;
    private bool prunePending;
    private bool pruneRequired;

    internal ProjectPersistenceCoordinator(TimeProvider timeProvider, Func<Action, Task> dispatch,
        Func<ProjectPersistenceState?> capture, Action<ProjectPersistenceSaveResult> saved,
        Action<Exception> reportError, IProjectPersistenceStorage? storage = null)
    {
        this.timeProvider = timeProvider;
        this.dispatch = dispatch;
        this.capture = capture;
        this.saved = saved;
        this.reportError = reportError;
        this.storage = storage ?? new ProjectPersistenceStorage();
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

        await pending.ConfigureAwait(false);
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
                disposal = DisposeCoreAsync(completion);
            }

            return new(disposal);
        }
    }

    private async Task DisposeCoreAsync(Task pending)
    {
        await pending.ConfigureAwait(false);
        lifetime.Dispose();
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
            if (disposed || !active || pauseCount != 0 || revision != scheduleVersion ||
                (autoSave ? autoSavePending : backupPending))
            {
                return;
            }

            if (autoSave)
            {
                autoSavePending = true;
            }
            else
            {
                backupPending = true;
            }

            var version = projectVersion;
            _ = EnqueueLocked(async () =>
            {
                try
                {
                    await RunTickAsync(autoSave, version, revision).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    await ReportAsync(error).ConfigureAwait(false);
                }
                finally
                {
                    lock (gate)
                    {
                        if (autoSave)
                        {
                            autoSavePending = false;
                        }
                        else
                        {
                            backupPending = false;
                        }
                    }
                }
                return true;
            });
        }
    }

    private async Task RunTickAsync(bool autoSave, long version, long revision)
    {
        if (!CanStart(version, revision))
        {
            return;
        }

        var state = await InvokeAsync(capture).ConfigureAwait(false);
        if (state is null || state.IsBusy || ProjectBackupStore.IsBackupPath(state.ProjectPath) ||
            !CanStart(version, revision))
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
                await storage.SaveAsync(state.Snapshot, state.ProjectPath, lifetime.Token).ConfigureAwait(false);
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
                    maximumCount, lifetime.Token).ConfigureAwait(false);
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
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                await ReportAsync(error).ConfigureAwait(false);
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
        });
    }

    private async Task PruneAsync(ProjectPersistenceState state, long version, int maximumCount)
    {
        await storage.PruneBackupsAsync(state.ProjectPath, maximumCount, lifetime.Token).ConfigureAwait(false);
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

    private Task<T> EnqueueLocked<T>(Func<Task<T>> operation)
    {
        var previous = completion;
        var pending = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            return await operation().ConfigureAwait(false);
        });
        completion = ObserveAsync(pending);
        return pending;
    }

    private async Task DrainAsync()
    {
        while (true)
        {
            Task pending;
            lock (gate)
            {
                pending = completion;
            }

            await pending.ConfigureAwait(false);
            lock (gate)
            {
                if (ReferenceEquals(pending, completion))
                {
                    return;
                }
            }
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
        return Convert.ToHexStringLower(SHA256.HashData(ProjectStore.Serialize(snapshot)));
    }
}
