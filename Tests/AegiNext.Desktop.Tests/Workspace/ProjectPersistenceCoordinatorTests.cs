using AegiNext.Application;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class ProjectPersistenceCoordinatorTests
{
    [Fact]
    public async Task AutoSaveAndBackupUseIndependentIntervalsAndSavedContentMarkers()
    {
        await using var context = new ProjectPersistenceTestContext();

        await context.AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Empty(context.Storage.SavedSnapshots);
        Assert.Empty(context.Storage.BackupSnapshots);
        await context.AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Single(context.Storage.SavedSnapshots);
        Assert.False(context.State!.HasUnsavedChanges);
        Assert.Empty(context.Storage.BackupSnapshots);
        await context.AdvanceAsync(TimeSpan.FromMinutes(3));

        Assert.Single(context.Storage.SavedSnapshots);
        Assert.Single(context.Storage.BackupSnapshots);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddMinutes(5), Assert.Single(context.Storage.BackupTimes));
        await context.AdvanceAsync(TimeSpan.FromMinutes(10));
        Assert.Single(context.Storage.SavedSnapshots);
        Assert.Single(context.Storage.BackupSnapshots);
        Assert.Empty(context.Errors);
    }

    [Fact]
    public async Task BusyAndMissingProjectsDoNotWriteAndTheNextIntervalRetries()
    {
        await using var context = new ProjectPersistenceTestContext();
        var state = context.State!;
        context.State = state with { IsBusy = true };

        await context.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Empty(context.Storage.SavedSnapshots);
        Assert.Empty(context.Storage.BackupSnapshots);
        context.State = null;
        await context.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Empty(context.Storage.SavedSnapshots);
        Assert.Empty(context.Storage.BackupSnapshots);
        context.State = state;
        await context.AdvanceAsync(TimeSpan.FromMinutes(5));

        Assert.Single(context.Storage.SavedSnapshots);
        Assert.Single(context.Storage.BackupSnapshots);
    }

    [Fact]
    public async Task SavingAnOlderSnapshotPreservesNewerEditsAndDoesNotStartOverlappingWork()
    {
        await using var context = new ProjectPersistenceTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        context.Clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var old = context.State!;
            var newer = old.Snapshot with { Name = "Newer committed edit" };
            context.State = old with { Snapshot = newer, HasUnsavedChanges = true };
            context.Clock.Advance(TimeSpan.FromMinutes(2));
            Assert.Equal(1, context.Storage.SaveAttempts);
            release.TrySetResult();
            await context.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Same(newer, context.State.Snapshot);
            Assert.True(context.State.HasUnsavedChanges);
            Assert.Equal("Committed", Assert.Single(context.Storage.SavedSnapshots).Name);
            context.Storage.SaveWork = null;
            await context.AdvanceAsync(TimeSpan.FromMinutes(2));
            Assert.Equal(newer, context.Storage.SavedSnapshots[1]);
            Assert.False(context.State.HasUnsavedChanges);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task FailedAutoSaveReportsItsErrorAndRetriesTheSameContentOnTheNextTick()
    {
        await using var context = new ProjectPersistenceTestContext();
        var failure = new IOException("Save failed once");
        context.Storage.SaveWork = _ => Task.FromException(failure);

        await context.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.Same(failure, Assert.Single(context.Errors));
        Assert.True(context.State!.HasUnsavedChanges);
        Assert.Empty(context.SaveResults);
        context.Storage.SaveWork = null;
        await context.AdvanceAsync(TimeSpan.FromMinutes(2));

        Assert.Equal(2, context.Storage.SaveAttempts);
        Assert.Single(context.Storage.SavedSnapshots);
        Assert.Single(context.SaveResults);
    }

    [Fact]
    public async Task AsynchronousDispatchFailureCompletesTheQueueAndAllowsTheNextTickToSave()
    {
        var failure = new IOException("Dispatch failed asynchronously");
        var reject = true;
        await using var context = new ProjectPersistenceTestContext(async action =>
        {
            await Task.Yield();
            if (reject)
            {
                reject = false;
                throw failure;
            }
            action();
        });

        await context.AdvanceAsync(TimeSpan.FromMinutes(2));

        Assert.Same(failure, Assert.Single(context.Errors));
        Assert.Empty(context.Storage.SavedSnapshots);
        await context.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.Single(context.Storage.SavedSnapshots);
        Assert.Single(context.SaveResults);
    }

    [Fact]
    public async Task ManualOperationPropagatesAsynchronousDispatchFailureWithoutPoisoningTheQueue()
    {
        var failure = new IOException("Dispatch failed asynchronously");
        var reject = true;
        await using var context = new ProjectPersistenceTestContext(async action =>
        {
            await Task.Yield();
            if (reject)
            {
                reject = false;
                throw failure;
            }
            action();
        });

        var observed = await Assert.ThrowsAsync<IOException>(() => context.Coordinator.RunExclusiveAsync(() =>
            Task.FromResult(1)));

        Assert.Same(failure, observed);
        Assert.Equal(2, await context.Coordinator.RunExclusiveAsync(() => Task.FromResult(2)));
    }

    [Fact]
    public async Task UndoingAfterAManualSaveWritesThePreviousAutoSavedContentBackToTheMainProject()
    {
        await using var context = new ProjectPersistenceTestContext();
        var first = context.State!.Snapshot;
        await context.AdvanceAsync(TimeSpan.FromMinutes(2));
        var manuallySaved = first with { Name = "Manually saved B" };
        await context.Coordinator.RunExclusiveAsync(async () =>
        {
            await context.Storage.SaveAsync(manuallySaved, context.State!.ProjectPath, CancellationToken.None);
            context.State = context.State with { Snapshot = manuallySaved, HasUnsavedChanges = false };
            await context.Coordinator.RecordManualSaveAsync(manuallySaved);
            return true;
        });
        context.State = context.State! with { Snapshot = first, HasUnsavedChanges = true };

        await context.AdvanceAsync(TimeSpan.FromMinutes(2));

        Assert.Collection(context.Storage.SavedSnapshots,
            snapshot => Assert.Equal("Committed", snapshot.Name),
            snapshot => Assert.Equal("Manually saved B", snapshot.Name),
            snapshot => Assert.Equal("Committed", snapshot.Name));
        Assert.False(context.State.HasUnsavedChanges);
        await context.AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Equal("Committed", Assert.Single(context.Storage.BackupSnapshots).Name);
    }

    [Fact]
    public async Task NestedPauseDrainsInFlightSaveAndOnlyTheLastLeaseRestartsTimers()
    {
        await using var context = new ProjectPersistenceTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        context.Clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var firstPause = context.Coordinator.PauseAsync();
            Assert.False(firstPause.IsCompleted);
            Assert.Equal(0, context.Clock.ActiveTimerCount);
            var secondPause = context.Coordinator.PauseAsync();
            release.TrySetResult();
            await using var first = await firstPause.WaitAsync(TimeSpan.FromSeconds(5));
            await using var second = await secondPause.WaitAsync(TimeSpan.FromSeconds(5));
            await first.DisposeAsync();
            Assert.Equal(0, context.Clock.ActiveTimerCount);
            await context.AdvanceAsync(TimeSpan.FromMinutes(10));
            Assert.Single(context.Storage.SavedSnapshots);
            Assert.Empty(context.Storage.BackupSnapshots);
            await second.DisposeAsync();
            Assert.Equal(2, context.Clock.ActiveTimerCount);
            await context.AdvanceAsync(TimeSpan.FromMinutes(5));
            Assert.Single(context.Storage.BackupSnapshots);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task ManualOperationsShareTheQueueAndAFailureDoesNotPoisonFollowingWork()
    {
        await using var context = new ProjectPersistenceTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = context.Coordinator.RunExclusiveAsync(async () =>
        {
            entered.TrySetResult();
            await release.Task;
            return 1;
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var second = context.Coordinator.RunExclusiveAsync(() => Task.FromResult(2));
            context.Clock.Advance(TimeSpan.FromMinutes(2));
            Assert.False(second.IsCompleted);
            Assert.Empty(context.Storage.SavedSnapshots);
            release.TrySetResult();
            Assert.Equal(1, await first.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(2, await second.WaitAsync(TimeSpan.FromSeconds(5)));
            await context.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Single(context.Storage.SavedSnapshots);
            await Assert.ThrowsAsync<IOException>(() => context.Coordinator.RunExclusiveAsync<int>(() =>
                Task.FromException<int>(new IOException("Manual failure"))));
            Assert.Equal(3, await context.Coordinator.RunExclusiveAsync(() => Task.FromResult(3)));
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task ActivatingAnotherGenerationResetsIndependentMarkersAndRejectsLateSavedCallbacks()
    {
        await using var context = new ProjectPersistenceTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        context.Clock.Advance(TimeSpan.FromMinutes(2));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.State = context.State! with { Generation = 2 };
            context.Coordinator.ActivateProject();
            release.TrySetResult();
            await context.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Empty(context.SaveResults);
            Assert.True(context.State.HasUnsavedChanges);
            context.Storage.SaveWork = null;
            await context.AdvanceAsync(TimeSpan.FromMinutes(5));

            Assert.Equal(2, context.Storage.SaveAttempts);
            Assert.Equal(2, Assert.Single(context.SaveResults).State.Generation);
            Assert.Single(context.Storage.BackupSnapshots);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task SettingsDisableAndRescheduleTimersAndLoweringRetentionPrunesWithoutWritingABackup()
    {
        await using var context = new ProjectPersistenceTestContext();
        context.Preferences = context.Preferences with
        {
            AutoSaveEnabled = false, BackupEnabled = false, MaximumBackupCount = 3
        };

        context.Coordinator.UpdatePreferences(context.Preferences);
        await context.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, context.Clock.ActiveTimerCount);
        Assert.Equal(3, Assert.Single(context.Storage.PrunedLimits));
        await context.AdvanceAsync(TimeSpan.FromMinutes(20));
        Assert.Empty(context.Storage.SavedSnapshots);
        Assert.Empty(context.Storage.BackupSnapshots);
        context.Preferences = context.Preferences with { AutoSaveEnabled = true, AutoSaveIntervalMinutes = 1 };
        context.Coordinator.UpdatePreferences(context.Preferences);
        Assert.Equal(1, context.Clock.ActiveTimerCount);
        await context.AdvanceAsync(TimeSpan.FromMinutes(1));
        Assert.Single(context.Storage.SavedSnapshots);
        Assert.Empty(context.Storage.BackupSnapshots);
    }

    [Fact]
    public async Task UnchangedPreferencesDoNotPostponeTheExistingDueTime()
    {
        await using var context = new ProjectPersistenceTestContext();
        await context.AdvanceAsync(TimeSpan.FromMinutes(1));

        context.Coordinator.UpdatePreferences(context.Preferences with { });
        await context.AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Single(context.Storage.SavedSnapshots);
    }

    [Fact]
    public async Task LoweringRetentionAgainDuringPruningAppliesTheLatestLimitInTheSameQueue()
    {
        await using var context = new ProjectPersistenceTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Storage.PruneWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        };
        context.Preferences = context.Preferences with { MaximumBackupCount = 10 };
        context.Coordinator.UpdatePreferences(context.Preferences);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            context.Preferences = context.Preferences with { MaximumBackupCount = 3 };
            context.Coordinator.UpdatePreferences(context.Preferences);
            Assert.Equal(10, Assert.Single(context.Storage.PrunedLimits));
            release.TrySetResult();
            await context.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Collection(context.Storage.PrunedLimits, limit => Assert.Equal(10, limit),
                limit => Assert.Equal(3, limit));
            Assert.Empty(context.Storage.BackupSnapshots);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task BackupPruneFailureRecordsSuccessfulContentAndRetriesOnlyPruning()
    {
        await using var context = new ProjectPersistenceTestContext();
        var failure = new ProjectBackupPruneException("backup.aeginext", new IOException("Prune failed"));
        context.Storage.BackupWork = _ => Task.FromException(failure);

        await context.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Same(failure, Assert.Single(context.Errors));
        Assert.Equal(1, context.Storage.BackupAttempts);
        context.Storage.BackupWork = null;
        await context.AdvanceAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(1, context.Storage.BackupAttempts);
        Assert.Single(context.Storage.PrunedLimits);
        Assert.Single(context.Errors);
    }

    [Fact]
    public async Task AutoSaveAndBackupKeepRawSnapshotsAndEquivalentMediaPathsDoNotRepeatWrites()
    {
        await using var context = new ProjectPersistenceTestContext();
        var state = context.State!;
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(state.ProjectDirectory, "video.mkv"));
        var original = state.Snapshot with { Assets = [media] };
        context.State = state with { Snapshot = original };
        context.Preferences = context.Preferences with { AutoSaveEnabled = false };
        context.Coordinator.UpdatePreferences(context.Preferences);

        await context.AdvanceAsync(TimeSpan.FromMinutes(5));
        Assert.Same(original, Assert.Single(context.Storage.BackupSnapshots));
        Assert.Equal(media, Assert.Single(Assert.Single(context.Storage.BackupSnapshots).Assets));
        context.Preferences = context.Preferences with { AutoSaveEnabled = true };
        context.Coordinator.UpdatePreferences(context.Preferences);
        await context.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.Same(original, Assert.Single(context.Storage.SavedSnapshots));
        Assert.Same(original, Assert.Single(context.SaveResults).PersistedSnapshot);
        Assert.Same(original, context.State!.Snapshot);
        Assert.Equal(media, Assert.Single(context.State.Snapshot.Assets));
        var equivalent = ProjectResources.NormalizeMediaReferences(original, state.ProjectDirectory);
        Assert.NotSame(original, equivalent);
        Assert.Equal("video.mkv", Assert.Single(equivalent.Assets).RelativePath);
        context.State = context.State with { Snapshot = equivalent, HasUnsavedChanges = true };
        await context.AdvanceAsync(TimeSpan.FromMinutes(2));
        Assert.Single(context.Storage.SavedSnapshots);
        Assert.Equal(2, context.SaveResults.Count);
        Assert.Same(equivalent, context.SaveResults[1].PersistedSnapshot);
        Assert.Same(equivalent, context.State.Snapshot);
        Assert.False(context.State.HasUnsavedChanges);
        await context.AdvanceAsync(TimeSpan.FromMinutes(1));

        Assert.Single(context.Storage.BackupSnapshots);
    }

    [Fact]
    public async Task DisposingStopsEveryTimerAndRejectsNewManualWork()
    {
        await using var context = new ProjectPersistenceTestContext();

        await context.Coordinator.DisposeAsync();
        Assert.Equal(0, context.Clock.ActiveTimerCount);
        context.Clock.Advance(TimeSpan.FromMinutes(20));
        await context.Coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Empty(context.Storage.SavedSnapshots);
        Assert.Empty(context.Storage.BackupSnapshots);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => context.Coordinator.RunExclusiveAsync(() =>
            Task.FromResult(1)));
    }

    [Fact]
    public async Task DisposalCancelsAnInFlightSaveAndDoesNotPublishALateSavedResult()
    {
        await using var context = new ProjectPersistenceTestContext();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.Storage.SaveWork = async cancellationToken =>
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        };
        context.Clock.Advance(TimeSpan.FromMinutes(2));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await context.Coordinator.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, context.Clock.ActiveTimerCount);
        Assert.Empty(context.Storage.SavedSnapshots);
        Assert.Empty(context.SaveResults);
        Assert.Empty(context.Errors);
    }
}
