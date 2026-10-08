using System.Collections.Concurrent;
using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

public sealed class AegiTaskServiceTests
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DynamicLimitDoesNotStopRunningWorkAndBarrierCannotBeOvertaken()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 2 };
        var firstEntered = Gate();
        var secondEntered = Gate();
        var firstRelease = Gate();
        var secondRelease = Gate();
        var barrierEntered = Gate();
        var barrierRelease = Gate();
        var laterEntered = Gate();
        var first = service.Submit(Held(firstEntered, firstRelease));
        var second = service.Submit(Held(secondEntered, secondRelease));
        await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(timeout);
        service.MaximumConcurrentTasks = 1;
        var barrier = service.Submit(Held(barrierEntered, barrierRelease, mode: AegiTaskMode.Blocking));
        var later = service.Submit(Held(laterEntered, Gate(completed: true)));
        Assert.Equal(AegiTaskState.Queued, barrier.Snapshot.State);
        firstRelease.SetResult();
        await first.Completion.WaitAsync(timeout);
        Assert.False(barrierEntered.Task.IsCompleted);
        Assert.False(laterEntered.Task.IsCompleted);
        secondRelease.SetResult();
        await barrierEntered.Task.WaitAsync(timeout);
        Assert.False(laterEntered.Task.IsCompleted);
        barrierRelease.SetResult();
        await Task.WhenAll(second.Completion, barrier.Completion, later.Completion).WaitAsync(timeout);
    }

    [Fact]
    public async Task ResourceHeadOfQueuePreventsIndependentTaskFromOvertakingAndAcquiresAllResources()
    {
        await using var service = new AegiTaskService();
        var resourceA = AegiTaskResource.Named("a");
        var resourceB = AegiTaskResource.Named("b");
        var firstEntered = Gate();
        var firstRelease = Gate();
        var secondEntered = Gate();
        var secondRelease = Gate();
        var thirdEntered = Gate();
        var fourthEntered = Gate();
        var order = new ConcurrentQueue<int>();
        var first = service.Submit(Held(firstEntered, firstRelease, resources: [resourceA]));
        await firstEntered.Task.WaitAsync(timeout);
        var second = service.Submit(Held(secondEntered, secondRelease, resources: [resourceA, resourceB], onStart: () => order.Enqueue(2)));
        var third = service.Submit(Held(thirdEntered, Gate(true), onStart: () => order.Enqueue(3)));
        var fourth = service.Submit(Held(fourthEntered, Gate(true), resources: [resourceB], onStart: () => order.Enqueue(4)));
        Assert.All(new[] { second, third, fourth }, handle => Assert.Equal(AegiTaskState.Queued, handle.Snapshot.State));
        firstRelease.SetResult();
        await Task.WhenAll(secondEntered.Task, thirdEntered.Task).WaitAsync(timeout);
        Assert.False(fourthEntered.Task.IsCompleted);
        secondRelease.SetResult();
        await Task.WhenAll(first.Completion, second.Completion, third.Completion, fourth.Completion).WaitAsync(timeout);
        Assert.Equal([2, 3, 4], order.ToArray());
    }

    [Fact]
    public async Task IncreaseStartsPendingWorkAndLimitIsValidated()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => service.MaximumConcurrentTasks = 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.MaximumConcurrentTasks = 33);
        var release = Gate();
        var firstEntered = Gate();
        var secondEntered = Gate();
        var first = service.Submit(Held(firstEntered, release));
        await firstEntered.Task.WaitAsync(timeout);
        var second = service.Submit(Held(secondEntered, release));
        Assert.Equal(AegiTaskState.Queued, second.Snapshot.State);
        service.MaximumConcurrentTasks = 2;
        await secondEntered.Task.WaitAsync(timeout);
        release.SetResult();
        await Task.WhenAll(first.Completion, second.Completion).WaitAsync(timeout);
    }

    [Fact]
    public async Task RunningCancellationRetainsResourceSlotAndEditingUntilCleanupEnds()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        service.RegisterScope("p", "Project");
        var entered = Gate();
        var cleaning = Gate();
        var cleanupRelease = Gate();
        var resource = AegiTaskResource.Named("controller");
        var operation = new TaskTestOperation(scopeId: "p", restriction: AegiTaskEditRestriction.Scope, resources: [resource])
        {
            Execute = async context =>
            {
                entered.SetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
                }
                finally
                {
                    cleaning.SetResult();
                    await cleanupRelease.Task;
                }
            },
        };
        var handle = service.Submit(operation);
        await entered.Task.WaitAsync(timeout);
        var laterEntered = Gate();
        var later = service.Submit(Held(laterEntered, Gate(true), resources: [resource]));
        Assert.True(handle.RequestCancel());
        await cleaning.Task.WaitAsync(timeout);
        Assert.Equal(AegiTaskState.Cancelling, handle.Snapshot.State);
        Assert.False(handle.RequestCancel());
        Assert.False(handle.Completion.IsCompleted);
        Assert.True(service.IsEditingRestricted("p"));
        Assert.False(laterEntered.Task.IsCompleted);
        cleanupRelease.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.Completion.WaitAsync(timeout));
        await later.Completion.WaitAsync(timeout);
        Assert.False(service.IsEditingRestricted("p"));
        Assert.Equal(AegiTaskState.Cancelled, handle.Snapshot.State);
    }

    [Fact]
    public async Task QueuedCancellationNeverAcquiresAnEditingLease()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        service.RegisterScope("p", "Project");
        var entered = Gate();
        var release = Gate();
        var first = service.Submit(Held(entered, release));
        await entered.Task.WaitAsync(timeout);
        var neverEntered = Gate();
        var queued = service.Submit(Held(neverEntered, Gate(true), scopeId: "p", restriction: AegiTaskEditRestriction.Scope));
        Assert.False(service.IsEditingRestricted("p"));
        Assert.True(queued.RequestCancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued.Completion);
        Assert.False(neverEntered.Task.IsCompleted);
        Assert.False(service.IsEditingRestricted("p"));
        release.SetResult();
        await first.Completion.WaitAsync(timeout);
    }

    [Fact]
    public async Task CommitRejectsCancellationAndWaitCancellationDoesNotCancelWork()
    {
        await using var service = new AegiTaskService();
        var committed = Gate();
        var release = Gate();
        var handle = service.Submit(new TaskTestResultOperation(async context =>
        {
            context.EnterCommit(() => true);
            committed.SetResult();
            await release.Task;
            return 42;
        }));
        await committed.Task.WaitAsync(timeout);
        Assert.Equal(AegiTaskState.Committing, handle.Snapshot.State);
        Assert.False(handle.RequestCancel());
        using var cancelledWait = new CancellationTokenSource();
        cancelledWait.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.WaitAsync(cancelledWait.Token));
        Assert.False(handle.Completion.IsCompleted);
        release.SetResult();
        Assert.Equal(42, await handle.Completion.WaitAsync(timeout));
    }

    [Fact]
    public async Task FailedValidityCancelsApplyAndExceptionReleasesAllLeases()
    {
        await using var service = new AegiTaskService();
        service.RegisterScope("p", "Project");
        var invalid = service.Submit(new TaskTestOperation(scopeId: "p", restriction: AegiTaskEditRestriction.Scope)
        {
            Execute = context =>
            {
                context.EnterCommit(() => false);
                return Task.CompletedTask;
            },
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => invalid.Completion.WaitAsync(timeout));
        Assert.False(service.IsEditingRestricted("p"));
        var failure = service.Submit(new TaskTestOperation(scopeId: "p", restriction: AegiTaskEditRestriction.Scope)
        {
            Execute = context =>
            {
                context.AcquireEditLease();
                throw new InvalidOperationException("boom");
            },
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => failure.Completion.WaitAsync(timeout));
        Assert.Equal(AegiTaskState.Failed, failure.Snapshot.State);
        Assert.Contains("boom", failure.Snapshot.ErrorSummary);
        Assert.False(service.IsEditingRestricted("p"));
    }

    [Fact]
    public async Task InternalStagesReuseParentResourcesAndUnawaitedStagesAreDrained()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var resource = AegiTaskResource.Named("write");
        var stageEntered = Gate();
        var stageRelease = Gate();
        var parent = service.Submit(new TaskTestOperation(resources: [resource])
        {
            Execute = context =>
            {
                Assert.Same(context, AegiTaskExecutionContext.Current);
                Assert.Throws<InvalidOperationException>(() => service.Submit(new TaskTestOperation()));
                Assert.Throws<InvalidOperationException>(() => context.RequireResources([AegiTaskResource.Named("other")]));
                _ = context.RunStageAsync("phase", async nested =>
                {
                    Assert.Same(context, nested);
                    nested.RequireResources([resource]);
                    stageEntered.SetResult();
                    await stageRelease.Task;
                }, [resource]);
                return Task.CompletedTask;
            },
        });
        await stageEntered.Task.WaitAsync(timeout);
        var next = service.Submit(new TaskTestOperation(resources: [resource]));
        Assert.False(parent.Completion.IsCompleted);
        Assert.Equal(AegiTaskState.Queued, next.Snapshot.State);
        stageRelease.SetResult();
        await Task.WhenAll(parent.Completion, next.Completion).WaitAsync(timeout);
    }

    [Fact]
    public async Task UnawaitedStageFailureIsObservedByParentHandle()
    {
        await using var service = new AegiTaskService();
        var parent = service.Submit(new TaskTestOperation
        {
            Execute = context =>
            {
                _ = context.RunStageAsync("failure", _ => Task.FromException(new IOException("stage failed")));
                return Task.CompletedTask;
            },
        });
        await Assert.ThrowsAsync<IOException>(() => parent.Completion.WaitAsync(timeout));
        Assert.Equal(AegiTaskState.Failed, parent.Snapshot.State);
    }

    [Fact]
    public async Task ClosingScopeCancelsItsWorkLeavesOtherScopeRunningAndSupportsFinalizationAndReopen()
    {
        await using var service = new AegiTaskService();
        service.RegisterScope("a", "A");
        service.RegisterScope("b", "B");
        var aEntered = Gate();
        var bEntered = Gate();
        var bRelease = Gate();
        var a = service.Submit(new TaskTestOperation(scopeId: "a")
        {
            Execute = async context =>
            {
                aEntered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, context.CancellationToken);
            },
        });
        var b = service.Submit(Held(bEntered, bRelease, scopeId: "b"));
        await Task.WhenAll(aEntered.Task, bEntered.Task).WaitAsync(timeout);
        using (var close = service.BeginCloseScope("a"))
        {
            Assert.Throws<InvalidOperationException>(() => service.Submit(new TaskTestOperation(scopeId: "a")));
            await close.DrainAsync().WaitAsync(timeout);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => a.Completion);
            Assert.False(b.Completion.IsCompleted);
            var persistence = close.SubmitFinalization(new TaskTestOperation(scopeId: "a", canCancel: false));
            await persistence.Completion.WaitAsync(timeout);
        }

        var afterReopen = service.Submit(new TaskTestOperation(scopeId: "a"));
        await afterReopen.Completion.WaitAsync(timeout);
        using (var finalClose = service.BeginCloseScope("a"))
        {
            await finalClose.DrainAsync();
            finalClose.CompleteClose();
        }

        Assert.Throws<InvalidOperationException>(() => service.Submit(new TaskTestOperation(scopeId: "a")));
        bRelease.SetResult();
        await b.Completion.WaitAsync(timeout);
    }

    [Fact]
    public async Task ScopeAndGlobalEditingLeasesUseReferenceCountsAndCoverLaterScopes()
    {
        await using var service = new AegiTaskService();
        service.RegisterScope("a", "A");
        using var first = service.AcquireScopeEditLease("a");
        using var second = service.AcquireScopeEditLease("a");
        first.Dispose();
        Assert.True(service.IsEditingRestricted("a"));
        second.Dispose();
        Assert.False(service.IsEditingRestricted("a"));
        var entered = Gate();
        var release = Gate();
        var global = service.Submit(Held(entered, release, restriction: AegiTaskEditRestriction.AllScopes));
        await entered.Task.WaitAsync(timeout);
        service.RegisterScope("later", "Later");
        Assert.True(service.IsEditingRestricted("later"));
        release.SetResult();
        await global.Completion.WaitAsync(timeout);
        Assert.False(service.IsEditingRestricted("later"));
    }

    [Fact]
    public async Task HistoryKeepsOnlyFiftyFinishedSnapshotsAndClearRetainsActiveRows()
    {
        await using var service = new AegiTaskService();
        for (var i = 0; i < 55; i++)
        {
            await service.Submit(new TaskTestOperation(name: "task " + i)).Completion.WaitAsync(timeout);
        }

        var snapshots = service.GetSnapshots();
        Assert.Equal(50, snapshots.Count);
        Assert.Equal("task 54", snapshots[0].Name);
        Assert.Equal("task 5", snapshots[^1].Name);
        var entered = Gate();
        var release = Gate();
        var active = service.Submit(Held(entered, release));
        await entered.Task.WaitAsync(timeout);
        service.ClearHistory();
        Assert.Equal(active.Id, Assert.Single(service.GetSnapshots()).Id);
        release.SetResult();
        await active.Completion.WaitAsync(timeout);
    }

    [Fact]
    public async Task PendingCoalescingSharesHandleButCannotCrossInterveningResourceOperationOrBarrier()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var entered = Gate();
        var release = Gate();
        var holder = service.Submit(Held(entered, release));
        await entered.Task.WaitAsync(timeout);
        var resource = AegiTaskResource.Named("target");
        var values = new ConcurrentQueue<int>();
        TaskTestOperation Write(int value, string? key = "latest") => new(resources: [resource], coalescingKey: key)
        {
            Execute = _ =>
            {
                values.Enqueue(value);
                return Task.CompletedTask;
            },
        };
        var first = service.Submit(Write(1));
        var replacement = service.Submit(Write(2));
        Assert.Same(first, replacement);
        var manual = service.Submit(Write(3, null));
        var afterManual = service.Submit(Write(4));
        Assert.NotSame(first, afterManual);
        var barrier = service.Submit(new TaskTestOperation(mode: AegiTaskMode.Blocking));
        var afterBarrier = service.Submit(Write(5));
        Assert.NotSame(afterManual, afterBarrier);
        release.SetResult();
        await Task.WhenAll(holder.Completion, first.Completion, manual.Completion, afterManual.Completion,
            barrier.Completion, afterBarrier.Completion).WaitAsync(timeout);
        Assert.Equal([2, 3, 4, 5], values.ToArray());
    }

    [Fact]
    public async Task SubmittingSynchronizationContextAndExecutionContextFlowAcrossAwait()
    {
        await using var service = new AegiTaskService();
        var synchronizationContext = new TaskTestSynchronizationContext();
        var previous = SynchronizationContext.Current;
        AegiTaskHandle handle;
        SynchronizationContext.SetSynchronizationContext(synchronizationContext);
        try
        {
            handle = service.Submit(new TaskTestOperation
            {
                Execute = async context =>
                {
                    Assert.Same(synchronizationContext, SynchronizationContext.Current);
                    Assert.Same(context, AegiTaskExecutionContext.Current);
                    await Task.Yield();
                    Assert.Same(synchronizationContext, SynchronizationContext.Current);
                    Assert.Same(context, AegiTaskExecutionContext.Current);
                },
            });
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        Assert.False(handle.Completion.IsCompleted);
        synchronizationContext.RunPostedCallbacks();
        await handle.Completion.WaitAsync(timeout);
        Assert.Null(AegiTaskExecutionContext.Current);
    }

    [Fact]
    public async Task FollowUpsRunAfterParentCleanupWithIndependentResourcesAndAreAbandonedOnFailure()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var parentEntered = Gate();
        var parentRelease = Gate();
        var childEntered = Gate();
        var submitted = new TaskCompletionSource<AegiTaskHandle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = service.Submit(new TaskTestOperation(resources: [AegiTaskResource.Named("parent")])
        {
            Execute = async context =>
            {
                context.ScheduleAfterCompletion(Held(childEntered, Gate(true), resources: [AegiTaskResource.Named("child")]),
                    handle => submitted.SetResult(handle));
                parentEntered.SetResult();
                await parentRelease.Task;
            },
        });
        await parentEntered.Task.WaitAsync(timeout);
        Assert.False(childEntered.Task.IsCompleted);
        Assert.False(submitted.Task.IsCompleted);
        parentRelease.SetResult();
        await parent.Completion.WaitAsync(timeout);
        var child = await submitted.Task.WaitAsync(timeout);
        Assert.NotNull(child);
        await child.Completion.WaitAsync(timeout);
        Assert.True(child.Snapshot.SubmissionSequence > parent.Snapshot.SubmissionSequence);
        var abandoned = new TaskCompletionSource<AegiTaskHandle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = service.Submit(new TaskTestOperation
        {
            Execute = context =>
            {
                context.ScheduleAfterCompletion(new TaskTestOperation(), handle => abandoned.SetResult(handle));
                throw new IOException("preparation failed");
            },
        });
        await Assert.ThrowsAsync<IOException>(() => failure.Completion.WaitAsync(timeout));
        Assert.Null(await abandoned.Task.WaitAsync(timeout));
    }

    [Fact]
    public async Task ScopeCloseCancelsEveryQueuedOperationBeforeAnyCanStart()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        service.RegisterScope("p", "Project");
        var holderEntered = Gate();
        var holderRelease = Gate();
        var holder = service.Submit(Held(holderEntered, holderRelease));
        await holderEntered.Task.WaitAsync(timeout);
        var firstEntered = Gate();
        var secondEntered = Gate();
        var first = service.Submit(Held(firstEntered, Gate(true), scopeId: "p"));
        var second = service.Submit(Held(secondEntered, Gate(true), scopeId: "p"));
        using var close = service.BeginCloseScope("p");
        await close.DrainAsync().WaitAsync(timeout);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Completion);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.Completion);
        Assert.False(firstEntered.Task.IsCompleted);
        Assert.False(secondEntered.Task.IsCompleted);
        holderRelease.SetResult();
        await holder.Completion.WaitAsync(timeout);
    }

    [Fact]
    public async Task PhaseLeaseDoesNotBlockPreparationAndFaultingObserverCannotDisruptTaskCleanup()
    {
        await using var service = new AegiTaskService();
        service.RegisterScope("p", "Project");
        service.Changed += (_, _) => throw new InvalidOperationException("observer failed");
        var prepared = Gate();
        var preparationRelease = Gate();
        var leased = Gate();
        var leaseRelease = Gate();
        var handle = service.Submit(new TaskTestOperation(scopeId: "p", restriction: AegiTaskEditRestriction.Scope)
        {
            RestrictEntireExecution = false,
            Execute = async context =>
            {
                prepared.SetResult();
                await preparationRelease.Task;
                using var lease = context.AcquireEditLease();
                leased.SetResult();
                await leaseRelease.Task;
            },
        });
        await prepared.Task.WaitAsync(timeout);
        Assert.False(service.IsEditingRestricted("p"));
        preparationRelease.SetResult();
        await leased.Task.WaitAsync(timeout);
        Assert.True(service.IsEditingRestricted("p"));
        leaseRelease.SetResult();
        await handle.Completion.WaitAsync(timeout);
        Assert.False(service.IsEditingRestricted("p"));
    }

    [Fact]
    public async Task CancellationCallbackMustFinishBeforeResourceAndSlotRelease()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var entered = Gate();
        var callbackEntered = Gate();
        var callbackRelease = Gate();
        var bodyRelease = Gate();
        var callbackRegistered = new TaskTestOperation
        {
            Execute = async context =>
            {
                context.CancellationToken.Register(() =>
                {
                    callbackEntered.SetResult();
                    callbackRelease.Task.GetAwaiter().GetResult();
                });
                entered.SetResult();
                await bodyRelease.Task;
            },
        };
        var handle = service.Submit(callbackRegistered);
        await entered.Task.WaitAsync(timeout);
        var laterEntered = Gate();
        var later = service.Submit(Held(laterEntered, Gate(true)));
        Assert.True(handle.RequestCancel());
        await callbackEntered.Task.WaitAsync(timeout);
        bodyRelease.SetResult();
        Assert.False(handle.Completion.IsCompleted);
        Assert.False(laterEntered.Task.IsCompleted);
        callbackRelease.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.Completion.WaitAsync(timeout));
        await later.Completion.WaitAsync(timeout);
    }

    [Fact]
    public async Task AcceptedCancellationPreventsEnteringCommitEvenWhenPreparationIgnoresToken()
    {
        await using var service = new AegiTaskService();
        var prepared = Gate();
        var release = Gate();
        var committed = false;
        var handle = service.Submit(new TaskTestOperation
        {
            Execute = async context =>
            {
                prepared.SetResult();
                await release.Task;
                context.EnterCommit();
                committed = true;
            },
        });
        await prepared.Task.WaitAsync(timeout);
        Assert.True(handle.RequestCancel());
        release.SetResult();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.Completion.WaitAsync(timeout));
        Assert.False(committed);
    }

    [Fact]
    public async Task ScopeCloseWaitsForNonCancellableCommitAndRejectsEarlyCompletion()
    {
        await using var service = new AegiTaskService();
        service.RegisterScope("p", "Project");
        var entered = Gate();
        var release = Gate();
        var handle = service.Submit(new TaskTestOperation(scopeId: "p", canCancel: false)
        {
            Execute = async context =>
            {
                context.EnterCommit();
                entered.SetResult();
                await release.Task;
            },
        });
        await entered.Task.WaitAsync(timeout);
        using var close = service.BeginCloseScope("p");
        var draining = close.DrainAsync();
        Assert.False(draining.IsCompleted);
        Assert.Throws<InvalidOperationException>(() => close.CompleteClose());
        Assert.False(handle.RequestCancel());
        release.SetResult();
        await draining.WaitAsync(timeout);
        close.CompleteClose();
        await handle.Completion.WaitAsync(timeout);
    }

    private static TaskCompletionSource Gate(bool completed = false)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        if (completed)
        {
            gate.SetResult();
        }

        return gate;
    }

    private static TaskTestOperation Held(TaskCompletionSource entered, TaskCompletionSource release,
        string? scopeId = null, AegiTaskMode mode = AegiTaskMode.Parallel,
        AegiTaskEditRestriction restriction = AegiTaskEditRestriction.None,
        IReadOnlyCollection<AegiTaskResource>? resources = null, Action? onStart = null)
    {
        return new(scopeId: scopeId, mode: mode, restriction: restriction, resources: resources)
        {
            Execute = async _ =>
            {
                onStart?.Invoke();
                entered.SetResult();
                await release.Task;
            },
        };
    }
}
