using System.Collections.Concurrent;
using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

public sealed class AegiTaskYieldTests
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task YieldKeepsIdentityResourcesAndFollowUpsBeforeAConflictAndBarrier()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var resource = AegiTaskResource.Named("cache");
        var entered = Gate();
        var yield = Gate();
        var resumed = Gate();
        var finish = Gate();
        var prefixEntered = Gate();
        var prefixRelease = Gate();
        var conflictEntered = Gate();
        var followUpSubmitted = new TaskCompletionSource<AegiTaskHandle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new ConcurrentQueue<string>();
        var executions = 0;
        var parent = service.Submit(new TaskTestOperation(resources: [resource])
        {
            Execute = async context =>
            {
                Interlocked.Increment(ref executions);
                context.ReportProgress(new("analysis", 1, 2));
                context.ScheduleAfterCompletion(Operation(() => order.Enqueue("follow-up"), resources: [resource]),
                    handle => followUpSubmitted.TrySetResult(handle));
                entered.TrySetResult();
                await yield.Task;
                await context.YieldIfWorkIsQueuedAsync();
                Assert.Same(context, AegiTaskExecutionContext.Current);
                context.RequireResources([resource]);
                order.Enqueue("resumed");
                resumed.TrySetResult();
                await finish.Task;
            }
        });
        await entered.Task.WaitAsync(timeout);
        var original = parent.Snapshot;
        var completion = parent.Completion;
        var prefix = service.Submit(Held(prefixEntered, prefixRelease, () => order.Enqueue("prefix")));
        var conflict = service.Submit(Operation(() =>
        {
            order.Enqueue("conflict");
            conflictEntered.TrySetResult();
        }, resources: [resource]));
        var barrier = service.Submit(Operation(() => order.Enqueue("barrier"), AegiTaskMode.Blocking));
        var later = service.Submit(Operation(() => order.Enqueue("later")));
        try
        {
            yield.TrySetResult();
            await prefixEntered.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, parent.Snapshot.State);
            Assert.Equal(original.Id, parent.Snapshot.Id);
            Assert.Equal(original.SubmissionSequence, parent.Snapshot.SubmissionSequence);
            Assert.Equal(original.StartedAt, parent.Snapshot.StartedAt);
            Assert.Equal(original.Progress, parent.Snapshot.Progress);
            Assert.Same(completion, parent.Completion);
            Assert.False(conflictEntered.Task.IsCompleted);
            Assert.False(followUpSubmitted.Task.IsCompleted);
            prefixRelease.TrySetResult();
            await resumed.Task.WaitAsync(timeout);
            Assert.Equal(original.StartedAt, parent.Snapshot.StartedAt);
            Assert.Equal(AegiTaskState.Running, parent.Snapshot.State);
            Assert.False(conflictEntered.Task.IsCompleted);
            finish.TrySetResult();
            await Task.WhenAll(parent.Completion, prefix.Completion, conflict.Completion, barrier.Completion, later.Completion)
                .WaitAsync(timeout);
            var followUp = await followUpSubmitted.Task.WaitAsync(timeout);
            Assert.NotNull(followUp);
            await followUp.Completion.WaitAsync(timeout);
            Assert.Equal(["prefix", "resumed", "conflict", "barrier", "later", "follow-up"], order.ToArray());
            Assert.Equal(1, executions);
            Assert.Single(service.GetSnapshots(), snapshot => snapshot.Id == parent.Id);
        }
        finally
        {
            yield.TrySetResult();
            prefixRelease.TrySetResult();
            finish.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task YieldedCancellationRetainsResourcesAndBlocksBarriersUntilCleanup(bool conflictingTask)
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var resource = AegiTaskResource.Named("cache");
        var entered = Gate();
        var yield = Gate();
        var cleaning = Gate();
        var cleanupRelease = Gate();
        var prefixEntered = Gate();
        var prefixRelease = Gate();
        var laterEntered = Gate();
        var resumed = false;
        var parent = service.Submit(new TaskTestOperation(resources: [resource])
        {
            Execute = async context =>
            {
                entered.TrySetResult();
                try
                {
                    await yield.Task;
                    await context.YieldIfWorkIsQueuedAsync();
                    resumed = true;
                }
                finally
                {
                    cleaning.TrySetResult();
                    await cleanupRelease.Task;
                }
            }
        });
        await entered.Task.WaitAsync(timeout);
        var prefix = service.Submit(Held(prefixEntered, prefixRelease));
        try
        {
            yield.TrySetResult();
            await prefixEntered.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, parent.Snapshot.State);
            Assert.True(parent.RequestCancel());
            await cleaning.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Cancelling, parent.Snapshot.State);
            Assert.False(parent.RequestCancel());
            Assert.False(parent.Completion.IsCompleted);
            prefixRelease.TrySetResult();
            await prefix.Completion.WaitAsync(timeout);
            var later = service.Submit(Operation(() => laterEntered.TrySetResult(),
                conflictingTask ? AegiTaskMode.Parallel : AegiTaskMode.Blocking,
                conflictingTask ? [resource] : null));
            Assert.Equal(AegiTaskState.Queued, later.Snapshot.State);
            Assert.False(laterEntered.Task.IsCompleted);
            cleanupRelease.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parent.Completion.WaitAsync(timeout));
            await later.Completion.WaitAsync(timeout);
            Assert.False(resumed);
            Assert.Equal(AegiTaskState.Cancelled, parent.Snapshot.State);
            Assert.Single(service.GetSnapshots(), snapshot => snapshot.Id == parent.Id);
        }
        finally
        {
            yield.TrySetResult();
            prefixRelease.TrySetResult();
            cleanupRelease.TrySetResult();
            parent.RequestCancel();
        }
    }

    [Fact]
    public async Task ClosingYieldedScopeDrainsCleanupWithoutCancellingOtherScopes()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        service.RegisterScope("analysis", "Analysis");
        service.RegisterScope("other", "Other");
        var entered = Gate();
        var yield = Gate();
        var cleaning = Gate();
        var cleanupRelease = Gate();
        var otherEntered = Gate();
        var otherRelease = Gate();
        var parent = service.Submit(new TaskTestOperation(scopeId: "analysis")
        {
            Execute = async context =>
            {
                entered.TrySetResult();
                try
                {
                    await yield.Task;
                    await context.YieldIfWorkIsQueuedAsync();
                }
                finally
                {
                    cleaning.TrySetResult();
                    await cleanupRelease.Task;
                }
            }
        });
        await entered.Task.WaitAsync(timeout);
        var other = service.Submit(Held(otherEntered, otherRelease, scopeId: "other"));
        try
        {
            yield.TrySetResult();
            await otherEntered.Task.WaitAsync(timeout);
            using var close = service.BeginCloseScope("analysis");
            await cleaning.Task.WaitAsync(timeout);
            var drain = close.DrainAsync();
            Assert.False(drain.IsCompleted);
            Assert.Throws<InvalidOperationException>(() => close.CompleteClose());
            Assert.Equal(AegiTaskState.Running, other.Snapshot.State);
            cleanupRelease.TrySetResult();
            await drain.WaitAsync(timeout);
            close.CompleteClose();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parent.Completion.WaitAsync(timeout));
            otherRelease.TrySetResult();
            await other.Completion.WaitAsync(timeout);
        }
        finally
        {
            yield.TrySetResult();
            cleanupRelease.TrySetResult();
            otherRelease.TrySetResult();
            parent.RequestCancel();
        }
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("conflict")]
    [InlineData("barrier")]
    public async Task YieldDoesNothingWhenThereIsNoIndependentPrefix(string boundary)
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var resource = AegiTaskResource.Named("cache");
        var entered = Gate();
        var release = Gate();
        var parent = service.Submit(new TaskTestOperation(resources: [resource])
        {
            Execute = async context =>
            {
                entered.TrySetResult();
                await release.Task;
                var yielding = context.YieldIfWorkIsQueuedAsync();
                Assert.True(yielding.IsCompletedSuccessfully);
                await yielding;
            }
        });
        await entered.Task.WaitAsync(timeout);
        var later = boundary switch
        {
            "conflict" => service.Submit(Operation(() => { }, resources: [resource])),
            "barrier" => service.Submit(Operation(() => { }, AegiTaskMode.Blocking)),
            _ => null
        };
        release.TrySetResult();
        await parent.Completion.WaitAsync(timeout);
        if (later is not null)
        {
            await later.Completion.WaitAsync(timeout);
        }
    }

    [Fact]
    public async Task YieldedEntryCannotBeCoalescedOrStartStagesOrCommit()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var resource = AegiTaskResource.Named("cache");
        var entered = new TaskCompletionSource<AegiTaskExecutionContext>(TaskCreationOptions.RunContinuationsAsynchronously);
        var yield = Gate();
        var prefixEntered = Gate();
        var prefixRelease = Gate();
        var originalExecutions = 0;
        var replacementExecutions = 0;
        var parent = service.Submit(new TaskTestOperation(resources: [resource], coalescingKey: "cache")
        {
            Execute = async context =>
            {
                Interlocked.Increment(ref originalExecutions);
                entered.TrySetResult(context);
                await yield.Task;
                await context.YieldIfWorkIsQueuedAsync();
            }
        });
        var context = await entered.Task.WaitAsync(timeout);
        var prefix = service.Submit(Held(prefixEntered, prefixRelease));
        try
        {
            yield.TrySetResult();
            await prefixEntered.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, parent.Snapshot.State);
            await Assert.ThrowsAsync<InvalidOperationException>(() => context.RunStageAsync("unexpected", _ => Task.CompletedTask));
            Assert.Throws<InvalidOperationException>(() => context.EnterCommit());
            Assert.Throws<InvalidOperationException>(() => context.AcquireEditLease());
            var replacement = service.Submit(new TaskTestOperation(resources: [resource], coalescingKey: "cache")
            {
                Execute = _ =>
                {
                    Interlocked.Increment(ref replacementExecutions);
                    return Task.CompletedTask;
                }
            });
            Assert.NotEqual(parent.Id, replacement.Id);
            prefixRelease.TrySetResult();
            await Task.WhenAll(parent.Completion, prefix.Completion, replacement.Completion).WaitAsync(timeout);
            Assert.Equal(1, originalExecutions);
            Assert.Equal(1, replacementExecutions);
        }
        finally
        {
            yield.TrySetResult();
            prefixRelease.TrySetResult();
            parent.RequestCancel();
        }
    }

    [Theory]
    [InlineData("blocking")]
    [InlineData("commit")]
    [InlineData("editing")]
    [InlineData("non-cancellable")]
    public async Task YieldRejectsUnsafeExecutionStates(string state)
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        service.RegisterScope("p", "Project");
        var entered = Gate();
        var release = Gate();
        var parent = service.Submit(new TaskTestOperation(scopeId: "p",
            mode: state == "blocking" ? AegiTaskMode.Blocking : AegiTaskMode.Parallel,
            canCancel: state != "non-cancellable",
            restriction: state == "editing" ? AegiTaskEditRestriction.Scope : AegiTaskEditRestriction.None)
        {
            Execute = async context =>
            {
                if (state == "commit")
                {
                    context.EnterCommit();
                }
                entered.TrySetResult();
                await release.Task;
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.YieldIfWorkIsQueuedAsync());
            }
        });
        await entered.Task.WaitAsync(timeout);
        var later = service.Submit(Operation(() => { }));
        release.TrySetResult();
        await Task.WhenAll(parent.Completion, later.Completion).WaitAsync(timeout);
    }

    [Fact]
    public async Task YieldRejectsUnfinishedStagesWithoutSealingLaterStages()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var entered = Gate();
        var release = Gate();
        var stageRelease = Gate();
        var parent = service.Submit(new TaskTestOperation
        {
            Execute = async context =>
            {
                var stage = context.RunStageAsync("fft", _ => stageRelease.Task);
                entered.TrySetResult();
                await release.Task;
                await Assert.ThrowsAsync<InvalidOperationException>(() => context.YieldIfWorkIsQueuedAsync());
                stageRelease.TrySetResult();
                await stage;
                await context.YieldIfWorkIsQueuedAsync();
                await context.RunStageAsync("next", _ => Task.CompletedTask);
            }
        });
        await entered.Task.WaitAsync(timeout);
        var later = service.Submit(Operation(() => { }));
        try
        {
            release.TrySetResult();
            await Task.WhenAll(parent.Completion, later.Completion).WaitAsync(timeout);
        }
        finally
        {
            release.TrySetResult();
            stageRelease.TrySetResult();
        }
    }

    [Fact]
    public async Task UnawaitedYieldIsDrainedBeforeResourcesAndTheTaskAreCompleted()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var resource = AegiTaskResource.Named("cache");
        var entered = Gate();
        var yield = Gate();
        var returned = Gate();
        var prefixEntered = Gate();
        var prefixRelease = Gate();
        var parent = service.Submit(new TaskTestOperation(resources: [resource])
        {
            Execute = async context =>
            {
                entered.TrySetResult();
                await yield.Task;
                _ = context.YieldIfWorkIsQueuedAsync();
                returned.TrySetResult();
            }
        });
        await entered.Task.WaitAsync(timeout);
        var prefix = service.Submit(Held(prefixEntered, prefixRelease));
        var conflict = service.Submit(Operation(() => { }, resources: [resource]));
        try
        {
            yield.TrySetResult();
            await Task.WhenAll(returned.Task, prefixEntered.Task).WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, parent.Snapshot.State);
            Assert.False(parent.Completion.IsCompleted);
            Assert.Equal(AegiTaskState.Queued, conflict.Snapshot.State);
            prefixRelease.TrySetResult();
            await Task.WhenAll(parent.Completion, prefix.Completion, conflict.Completion).WaitAsync(timeout);
            Assert.Single(service.GetSnapshots(), snapshot => snapshot.Id == parent.Id);
        }
        finally
        {
            yield.TrySetResult();
            prefixRelease.TrySetResult();
            conflict.RequestCancel();
            parent.RequestCancel();
        }
    }

    [Fact]
    public async Task RepeatedYieldsResumeOneExecutionAndRetainTheOriginalStartTime()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var entered = Gate();
        var firstYield = Gate();
        var secondBoundary = Gate();
        var secondYield = Gate();
        var firstEntered = Gate();
        var firstRelease = Gate();
        var secondEntered = Gate();
        var secondRelease = Gate();
        var executions = 0;
        var parent = service.Submit(new TaskTestOperation
        {
            Execute = async context =>
            {
                Interlocked.Increment(ref executions);
                entered.TrySetResult();
                await firstYield.Task;
                await context.YieldIfWorkIsQueuedAsync();
                await context.RunStageAsync("next chunk", _ => Task.CompletedTask);
                secondBoundary.TrySetResult();
                await secondYield.Task;
                await context.YieldIfWorkIsQueuedAsync();
            }
        });
        await entered.Task.WaitAsync(timeout);
        var startedAt = parent.Snapshot.StartedAt;
        var first = service.Submit(Held(firstEntered, firstRelease));
        try
        {
            firstYield.TrySetResult();
            await firstEntered.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, parent.Snapshot.State);
            firstRelease.TrySetResult();
            await secondBoundary.Task.WaitAsync(timeout);
            var second = service.Submit(Held(secondEntered, secondRelease));
            secondYield.TrySetResult();
            await secondEntered.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, parent.Snapshot.State);
            Assert.Equal(startedAt, parent.Snapshot.StartedAt);
            secondRelease.TrySetResult();
            await Task.WhenAll(parent.Completion, first.Completion, second.Completion).WaitAsync(timeout);
            Assert.Equal(1, executions);
            Assert.Equal(startedAt, parent.Snapshot.StartedAt);
            Assert.Single(service.GetSnapshots(), snapshot => snapshot.Id == parent.Id);
        }
        finally
        {
            firstYield.TrySetResult();
            secondYield.TrySetResult();
            firstRelease.TrySetResult();
            secondRelease.TrySetResult();
            parent.RequestCancel();
        }
    }

    [Fact]
    public async Task YieldedCancellationWaitsForCallbacksAndAbandonsFollowUps()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        var entered = Gate();
        var yield = Gate();
        var callbackEntered = Gate();
        var callbackRelease = Gate();
        var cleaned = Gate();
        var prefixEntered = Gate();
        var prefixRelease = Gate();
        var barrierEntered = Gate();
        var followUpSubmitted = new TaskCompletionSource<AegiTaskHandle?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var parent = service.Submit(new TaskTestOperation
        {
            Execute = async context =>
            {
                context.CancellationToken.Register(() =>
                {
                    callbackEntered.TrySetResult();
                    callbackRelease.Task.GetAwaiter().GetResult();
                });
                context.ScheduleAfterCompletion(Operation(() => throw new InvalidOperationException("Cancelled follow-up ran.")),
                    handle => followUpSubmitted.TrySetResult(handle));
                entered.TrySetResult();
                try
                {
                    await yield.Task;
                    await context.YieldIfWorkIsQueuedAsync();
                }
                finally
                {
                    cleaned.TrySetResult();
                }
            }
        });
        await entered.Task.WaitAsync(timeout);
        var prefix = service.Submit(Held(prefixEntered, prefixRelease));
        try
        {
            yield.TrySetResult();
            await prefixEntered.Task.WaitAsync(timeout);
            parent.RequestCancel();
            await Task.WhenAll(callbackEntered.Task, cleaned.Task).WaitAsync(timeout);
            prefixRelease.TrySetResult();
            await prefix.Completion.WaitAsync(timeout);
            var barrier = service.Submit(Operation(() => barrierEntered.TrySetResult(), AegiTaskMode.Blocking));
            Assert.False(parent.Completion.IsCompleted);
            Assert.False(barrierEntered.Task.IsCompleted);
            Assert.False(followUpSubmitted.Task.IsCompleted);
            callbackRelease.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => parent.Completion.WaitAsync(timeout));
            await barrier.Completion.WaitAsync(timeout);
            Assert.Null(await followUpSubmitted.Task.WaitAsync(timeout));
        }
        finally
        {
            yield.TrySetResult();
            prefixRelease.TrySetResult();
            callbackRelease.TrySetResult();
            parent.RequestCancel();
        }
    }

    [Fact]
    public async Task MultipleYieldedExecutionsRespectChangedLimitsAndRetainTheirResourceLeases()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 2 };
        var firstEntered = Gate();
        var secondEntered = Gate();
        var firstYield = Gate();
        var secondYield = Gate();
        var firstResumed = Gate();
        var secondResumed = Gate();
        var finish = Gate();
        var prefixEntered = Gate();
        var prefixRelease = Gate();
        var first = service.Submit(new TaskTestOperation(resources: [AegiTaskResource.Named("first cache")])
        {
            Execute = async context =>
            {
                firstEntered.TrySetResult();
                await firstYield.Task;
                await context.YieldIfWorkIsQueuedAsync();
                firstResumed.TrySetResult();
                await finish.Task;
            }
        });
        var second = service.Submit(new TaskTestOperation(resources: [AegiTaskResource.Named("second cache")])
        {
            Execute = async context =>
            {
                secondEntered.TrySetResult();
                await secondYield.Task;
                await context.YieldIfWorkIsQueuedAsync();
                secondResumed.TrySetResult();
                await finish.Task;
            }
        });
        await Task.WhenAll(firstEntered.Task, secondEntered.Task).WaitAsync(timeout);
        var prefix = service.Submit(Held(prefixEntered, prefixRelease));
        var secondYielded = Gate();
        service.Changed += (_, _) =>
        {
            if (second.Snapshot.State == AegiTaskState.Yielded)
            {
                secondYielded.TrySetResult();
            }
        };
        try
        {
            firstYield.TrySetResult();
            await prefixEntered.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, first.Snapshot.State);
            service.MaximumConcurrentTasks = 1;
            secondYield.TrySetResult();
            await secondYielded.Task.WaitAsync(timeout);
            Assert.False(firstResumed.Task.IsCompleted);
            Assert.False(secondResumed.Task.IsCompleted);
            service.MaximumConcurrentTasks = 2;
            await firstResumed.Task.WaitAsync(timeout);
            Assert.False(secondResumed.Task.IsCompleted);
            prefixRelease.TrySetResult();
            await secondResumed.Task.WaitAsync(timeout);
            finish.TrySetResult();
            await Task.WhenAll(first.Completion, second.Completion, prefix.Completion).WaitAsync(timeout);
            Assert.Equal(3, service.GetSnapshots().Count);
        }
        finally
        {
            firstYield.TrySetResult();
            secondYield.TrySetResult();
            prefixRelease.TrySetResult();
            finish.TrySetResult();
            first.RequestCancel();
            second.RequestCancel();
        }
    }

    [Fact]
    public async Task ResumeWaitsForEarlierDispatchPostedToAnotherSynchronizationContext()
    {
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 2 };
        var entered = Gate();
        var yield = Gate();
        var yielded = Gate();
        var resuming = Gate();
        var resumed = Gate();
        var blockerEntered = Gate();
        var blockerRelease = Gate();
        var prefixEntered = Gate();
        var context = new TaskTestSynchronizationContext();
        var parent = service.Submit(new TaskTestOperation
        {
            Execute = async execution =>
            {
                entered.TrySetResult();
                await yield.Task;
                var pending = execution.YieldIfWorkIsQueuedAsync();
                yielded.TrySetResult();
                await pending;
                resumed.TrySetResult();
            }
        });
        var blocker = service.Submit(Held(blockerEntered, blockerRelease));
        await Task.WhenAll(entered.Task, blockerEntered.Task).WaitAsync(timeout);
        AegiTaskHandle prefix;
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            prefix = service.Submit(Operation(() => prefixEntered.TrySetResult()));
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
        service.Changed += (_, _) =>
        {
            if (yielded.Task.IsCompleted && parent.Snapshot.State == AegiTaskState.Running)
            {
                resuming.TrySetResult();
            }
        };
        try
        {
            yield.TrySetResult();
            await yielded.Task.WaitAsync(timeout);
            Assert.Equal(AegiTaskState.Yielded, parent.Snapshot.State);
            blockerRelease.TrySetResult();
            await resuming.Task.WaitAsync(timeout);
            Assert.False(prefixEntered.Task.IsCompleted);
            Assert.False(resumed.Task.IsCompleted);
            context.RunPostedCallbacks();
            await Task.WhenAll(parent.Completion, blocker.Completion, prefix.Completion).WaitAsync(timeout);
            Assert.True(resumed.Task.IsCompletedSuccessfully);
        }
        finally
        {
            yield.TrySetResult();
            blockerRelease.TrySetResult();
            context.RunPostedCallbacks();
            parent.RequestCancel();
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static TaskTestOperation Operation(Action execute, AegiTaskMode mode = AegiTaskMode.Parallel,
        IReadOnlyCollection<AegiTaskResource>? resources = null)
    {
        return new(mode: mode, resources: resources)
        {
            Execute = _ =>
            {
                execute();
                return Task.CompletedTask;
            }
        };
    }

    private static TaskTestOperation Held(TaskCompletionSource entered, TaskCompletionSource release,
        Action? execute = null, string? scopeId = null)
    {
        return new(scopeId: scopeId)
        {
            Execute = async _ =>
            {
                execute?.Invoke();
                entered.TrySetResult();
                await release.Task;
            }
        };
    }
}
