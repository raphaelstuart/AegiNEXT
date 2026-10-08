using System.Collections.Concurrent;
using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

public sealed class AegiTaskStorageResolutionTests
{
    private static TimeSpan Timeout { get; } = TimeSpan.FromSeconds(10);

    private static int[] UnknownBoundaryOrder { get; } = [1, 3, 2];

    [Fact]
    public async Task DeferredResolutionLeavesSubmittingContextResponsiveAndRestoresItForExecution()
    {
        var resolver = new TaskTestStorageResolver { BlockSynchronously = true };
        await using var service = new AegiTaskService(resolver.ResolveAsync);
        service.RegisterScope("p", "Project");
        var (declared, resolved) = CreateStorageResources();
        var synchronizationContext = new TaskTestSynchronizationContext();
        var uiCallback = Gate();
        var executed = Gate();
        var submission = Task.Run(() =>
        {
            var previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(synchronizationContext);
            try
            {
                var submitted = service.Submit(new TaskTestOperation(scopeId: "p", resources: [declared],
                    restriction: AegiTaskEditRestriction.Scope)
                {
                    Execute = async context =>
                    {
                        Assert.Same(synchronizationContext, SynchronizationContext.Current);
                        Assert.Same(context, AegiTaskExecutionContext.Current);
                        context.RequireResources([declared]);
                        await Task.Yield();
                        Assert.Same(synchronizationContext, SynchronizationContext.Current);
                        Assert.Same(context, AegiTaskExecutionContext.Current);
                        executed.TrySetResult();
                    }
                });
                synchronizationContext.Post(_ => uiCallback.TrySetResult(), null);
                return submitted;
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(previous);
            }
        });

        AegiTaskHandle? handle = null;
        try
        {
            handle = await submission.WaitAsync(Timeout);
            Assert.Equal(declared, await resolver.Entered.WaitAsync(Timeout));
            Assert.True(resolver.ExecutedOnThreadPool);
            Assert.NotSame(synchronizationContext, resolver.ExecutionSynchronizationContext);
            Assert.Equal(AegiTaskState.Queued, handle.Snapshot.State);
            Assert.Null(handle.Snapshot.StartedAt);
            Assert.False(service.IsEditingRestricted("p"));
            Assert.Single(service.GetSnapshots());
            synchronizationContext.RunPostedCallbacks();
            Assert.True(uiCallback.Task.IsCompletedSuccessfully);
            Assert.False(executed.Task.IsCompleted);
            var dispatched = Gate();
            service.Changed += (_, _) =>
            {
                if (handle.Snapshot.State == AegiTaskState.Running)
                {
                    dispatched.TrySetResult();
                }
            };
            resolver.Complete(resolved);
            await dispatched.Task.WaitAsync(Timeout);
            synchronizationContext.RunPostedCallbacks();
            await handle.Completion.WaitAsync(Timeout);
            Assert.True(executed.Task.IsCompletedSuccessfully);
            Assert.False(service.IsEditingRestricted("p"));
        }
        finally
        {
            resolver.Complete(resolved);
            handle ??= await submission.WaitAsync(Timeout);
            handle.RequestCancel();
            synchronizationContext.RunPostedCallbacks();
        }
    }

    [Fact]
    public async Task UnresolvedQueueHeadPreservesFifoAndAcquiresItsWholeResolvedResourceSet()
    {
        var resolver = new TaskTestStorageResolver();
        await using var service = new AegiTaskService(resolver.ResolveAsync);
        var (declared, resolved) = CreateStorageResources();
        var otherResource = AegiTaskResource.Named("additional resource");
        var entered = Gate();
        var release = Gate();
        var independentEntered = Gate();
        var conflictingEntered = Gate();
        var barrierEntered = Gate();
        var order = new ConcurrentQueue<string>();
        var first = service.Submit(new TaskTestOperation(resources: [declared, otherResource])
        {
            Execute = async context =>
            {
                context.RequireResources([declared, otherResource]);
                order.Enqueue("first");
                entered.TrySetResult();
                await release.Task;
            }
        });
        try
        {
            await resolver.Entered.WaitAsync(Timeout);
            var independent = service.Submit(new TaskTestOperation
            {
                Execute = _ =>
                {
                    order.Enqueue("independent");
                    independentEntered.TrySetResult();
                    return Task.CompletedTask;
                }
            });
            var conflict = service.Submit(new TaskTestOperation(resources: [resolved, otherResource])
            {
                Execute = _ =>
                {
                    order.Enqueue("conflict");
                    conflictingEntered.TrySetResult();
                    return Task.CompletedTask;
                }
            });
            var barrier = service.Submit(new TaskTestOperation(mode: AegiTaskMode.Blocking)
            {
                Execute = _ =>
                {
                    order.Enqueue("barrier");
                    barrierEntered.TrySetResult();
                    return Task.CompletedTask;
                }
            });
            var later = service.Submit(new TaskTestOperation
            {
                Execute = _ =>
                {
                    order.Enqueue("later");
                    return Task.CompletedTask;
                }
            });
            Assert.All(new[] { first, independent, conflict, barrier, later },
                handle => Assert.Equal(AegiTaskState.Queued, handle.Snapshot.State));
            Assert.False(independentEntered.Task.IsCompleted);
            resolver.Complete(resolved);
            await entered.Task.WaitAsync(Timeout);
            await independentEntered.Task.WaitAsync(Timeout);
            Assert.False(conflictingEntered.Task.IsCompleted);
            Assert.False(barrierEntered.Task.IsCompleted);
            release.TrySetResult();
            await Task.WhenAll(first.Completion, independent.Completion, conflict.Completion, barrier.Completion, later.Completion)
                .WaitAsync(Timeout);
            Assert.Equal(["first", "independent", "conflict", "barrier", "later"], order.ToArray());
        }
        finally
        {
            resolver.Complete(resolved);
            release.TrySetResult();
        }
    }

    [Fact]
    public async Task QueuedCancellationCompletesBeforeResolverReturnsAndDoesNotAcquireEditingOrSlots()
    {
        var resolver = new TaskTestStorageResolver();
        await using var service = new AegiTaskService(resolver.ResolveAsync) { MaximumConcurrentTasks = 1 };
        service.RegisterScope("p", "Project");
        var (declared, resolved) = CreateStorageResources();
        var executions = 0;
        var handle = service.Submit(new TaskTestOperation(scopeId: "p", resources: [declared],
            restriction: AegiTaskEditRestriction.Scope)
        {
            Execute = context =>
            {
                context.EnterCommit();
                Interlocked.Increment(ref executions);
                return Task.CompletedTask;
            }
        });
        try
        {
            await resolver.Entered.WaitAsync(Timeout);
            Assert.False(service.IsEditingRestricted("p"));
            Assert.True(handle.RequestCancel());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handle.Completion.WaitAsync(Timeout));
            Assert.Equal(AegiTaskState.Cancelled, handle.Snapshot.State);
            Assert.False(handle.RequestCancel());
            Assert.False(service.IsEditingRestricted("p"));
            var later = service.Submit(new TaskTestOperation(resources: [resolved]));
            await later.Completion.WaitAsync(Timeout);
            Assert.False(resolver.Returned.IsCompleted);
            resolver.Complete(resolved);
            await resolver.Returned.WaitAsync(Timeout);
            await service.DrainAsync().WaitAsync(Timeout);
            Assert.Equal(0, executions);
            Assert.Equal(AegiTaskState.Cancelled, handle.Snapshot.State);
            Assert.Null(handle.Snapshot.StartedAt);
            Assert.False(service.IsEditingRestricted("p"));
            Assert.Single(service.GetSnapshots(), snapshot => snapshot.Id == handle.Id);
        }
        finally
        {
            resolver.Complete(resolved);
            handle.RequestCancel();
        }
    }

    [Fact]
    public async Task ClosingScopeIgnoresLateResolutionAfterReopeningAndLeavesOtherScopeRunning()
    {
        var resolver = new TaskTestStorageResolver();
        await using var service = new AegiTaskService(resolver.ResolveAsync);
        service.RegisterScope("p", "Project");
        service.RegisterScope("other", "Other project");
        var otherEntered = Gate();
        var otherRelease = Gate();
        var other = service.Submit(new TaskTestOperation(scopeId: "other")
        {
            Execute = async _ =>
            {
                otherEntered.TrySetResult();
                await otherRelease.Task;
            }
        });
        await otherEntered.Task.WaitAsync(Timeout);
        var (declared, resolved) = CreateStorageResources();
        var executions = 0;
        var pending = service.Submit(new TaskTestOperation(scopeId: "p", resources: [declared])
        {
            Execute = _ =>
            {
                Interlocked.Increment(ref executions);
                return Task.CompletedTask;
            }
        });
        try
        {
            await resolver.Entered.WaitAsync(Timeout);
            using (var close = service.BeginCloseScope("p"))
            {
                Assert.Throws<InvalidOperationException>(() => service.Submit(new TaskTestOperation(scopeId: "p")));
                await close.DrainAsync().WaitAsync(Timeout);
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.Completion);
                Assert.False(resolver.Returned.IsCompleted);
                Assert.False(other.Completion.IsCompleted);
                var finalization = close.SubmitFinalization(new TaskTestOperation(scopeId: "p", canCancel: false));
                await finalization.Completion.WaitAsync(Timeout);
            }

            var reopened = service.Submit(new TaskTestOperation(scopeId: "p", resources: [resolved]));
            await reopened.Completion.WaitAsync(Timeout);
            resolver.Complete(resolved);
            await resolver.Returned.WaitAsync(Timeout);
            Assert.Equal(0, executions);
            Assert.Equal(AegiTaskState.Cancelled, pending.Snapshot.State);
            Assert.Null(pending.Snapshot.StartedAt);
            Assert.False(service.IsEditingRestricted("p"));
            otherRelease.TrySetResult();
            await other.Completion.WaitAsync(Timeout);
        }
        finally
        {
            resolver.Complete(resolved);
            otherRelease.TrySetResult();
            pending.RequestCancel();
        }
    }

    [Fact]
    public async Task ResolutionFailureFailsOnlyItsTaskAndRestoresQueueDispatch()
    {
        var resolver = new TaskTestStorageResolver();
        await using var service = new AegiTaskService(resolver.ResolveAsync);
        service.RegisterScope("p", "Project");
        var (declared, resolved) = CreateStorageResources();
        var executions = 0;
        var failed = service.Submit(new TaskTestOperation(scopeId: "p", resources: [declared],
            restriction: AegiTaskEditRestriction.Scope)
        {
            Execute = _ =>
            {
                Interlocked.Increment(ref executions);
                return Task.CompletedTask;
            }
        });
        try
        {
            await resolver.Entered.WaitAsync(Timeout);
            var later = service.Submit(new TaskTestOperation(resources: [resolved]));
            Assert.Equal(AegiTaskState.Queued, later.Snapshot.State);
            resolver.Fail(new IOException("storage resolution failed"));
            var error = await Assert.ThrowsAsync<IOException>(() => failed.Completion.WaitAsync(Timeout));
            Assert.Equal("storage resolution failed", error.Message);
            Assert.Equal(AegiTaskState.Failed, failed.Snapshot.State);
            Assert.Contains("storage resolution failed", failed.Snapshot.ErrorSummary);
            Assert.Null(failed.Snapshot.StartedAt);
            await later.Completion.WaitAsync(Timeout);
            Assert.Equal(0, executions);
            Assert.False(service.IsEditingRestricted("p"));
        }
        finally
        {
            resolver.Complete(resolved);
            failed.RequestCancel();
        }
    }

    [Fact]
    public async Task DeferredDanglingAliasSerializesWritesAcrossProjectScopes()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aegi-task-deferred-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var target = Path.Combine(directory, "pending.json");
            var alias = Path.Combine(directory, "alias.json");
            File.CreateSymbolicLink(alias, target);
            Assert.False(File.Exists(target));
            var aliasDeclaration = AegiTaskResource.DeferredStoragePath(alias);
            var aliasResolved = new TaskCompletionSource<AegiTaskResource>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var service = new AegiTaskService(resource =>
            {
                var identity = resource.Resolve();
                if (resource == aliasDeclaration)
                {
                    aliasResolved.TrySetResult(identity);
                }

                return Task.FromResult(identity);
            });
            service.RegisterScope("first", "First project");
            service.RegisterScope("second", "Second project");
            var entered = Gate();
            var release = Gate();
            var secondEntered = Gate();
            var first = service.Submit(new TaskTestOperation(scopeId: "first",
                resources: [AegiTaskResource.Project("first"), AegiTaskResource.DeferredStoragePath(target)])
            {
                Execute = async _ =>
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            });
            try
            {
                await entered.Task.WaitAsync(Timeout);
                var second = service.Submit(new TaskTestOperation(scopeId: "second",
                    resources: [AegiTaskResource.Project("second"), aliasDeclaration])
                {
                    Execute = _ =>
                    {
                        secondEntered.TrySetResult();
                        return Task.CompletedTask;
                    }
                });
                Assert.Equal(AegiTaskResource.StoragePath(target), await aliasResolved.Task.WaitAsync(Timeout));
                Assert.Equal(AegiTaskState.Queued, second.Snapshot.State);
                Assert.False(secondEntered.Task.IsCompleted);
                release.TrySetResult();
                await Task.WhenAll(first.Completion, second.Completion).WaitAsync(Timeout);
                Assert.True(secondEntered.Task.IsCompletedSuccessfully);
            }
            finally
            {
                release.TrySetResult();
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task YieldDoesNotTreatAnUnresolvedStorageQueueHeadAsIndependentWork()
    {
        var resolver = new TaskTestStorageResolver();
        await using var service = new AegiTaskService(resolver.ResolveAsync) { MaximumConcurrentTasks = 1 };
        var (declared, resolved) = CreateStorageResources();
        var entered = Gate();
        var yield = Gate();
        var resumed = Gate();
        var release = Gate();
        var conflictEntered = Gate();
        var parent = service.Submit(new TaskTestOperation(resources: [resolved])
        {
            Execute = async context =>
            {
                entered.TrySetResult();
                await yield.Task;
                var yielding = context.YieldIfWorkIsQueuedAsync();
                Assert.True(yielding.IsCompletedSuccessfully);
                await yielding;
                resumed.TrySetResult();
                await release.Task;
            }
        });
        await entered.Task.WaitAsync(Timeout);
        var conflict = service.Submit(new TaskTestOperation(resources: [declared])
        {
            Execute = _ =>
            {
                conflictEntered.TrySetResult();
                return Task.CompletedTask;
            }
        });
        try
        {
            yield.TrySetResult();
            await resumed.Task.WaitAsync(Timeout);
            Assert.Equal(AegiTaskState.Running, parent.Snapshot.State);
            Assert.False(conflictEntered.Task.IsCompleted);
            resolver.Complete(resolved);
            release.TrySetResult();
            await Task.WhenAll(parent.Completion, conflict.Completion).WaitAsync(Timeout);
            Assert.True(conflictEntered.Task.IsCompletedSuccessfully);
        }
        finally
        {
            yield.TrySetResult();
            release.TrySetResult();
            resolver.Complete(resolved);
            parent.RequestCancel();
            conflict.RequestCancel();
        }
    }

    [Fact]
    public async Task DeferredDeclarationAcceptsAStorageLinkCycleWithoutResolvingAndCanonicalResourcesBypassResolver()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aegi-task-declaration-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "first.json");
            var second = Path.Combine(directory, "second.json");
            File.CreateSymbolicLink(first, second);
            File.CreateSymbolicLink(second, first);
            Assert.ThrowsAny<IOException>(() => AegiTaskResource.StoragePath(first));
            var deferred = AegiTaskResource.DeferredStoragePath(first);
            Assert.Equal(deferred, AegiTaskResource.DeferredStoragePath(Path.Combine(directory, "child", "..", "first.json")));
            Assert.Throws<ArgumentException>(() => AegiTaskResource.DeferredStoragePath(" "));
            var resolverCalls = 0;
            await using var service = new AegiTaskService(resource =>
            {
                Interlocked.Increment(ref resolverCalls);
                return Task.FromException<AegiTaskResource>(new InvalidOperationException("Canonical resources must bypass resolution."));
            });
            var canonical = AegiTaskResource.StoragePath(Path.Combine(directory, "target.json"));
            var handle = service.Submit(new TaskTestOperation(resources: [canonical, AegiTaskResource.Named("other")]));
            await handle.Completion.WaitAsync(Timeout);
            Assert.Equal(0, resolverCalls);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("same declaration")]
    [InlineData("different alias declaration")]
    [InlineData("unknown boundary")]
    public async Task PendingCoalescingRequiresExactDeclarationsAndCannotCrossUnknownResources(string boundary)
    {
        var resolver = new TaskTestStorageResolver();
        var (declared, resolved) = CreateStorageResources();
        var alias = AegiTaskResource.DeferredStoragePath(Path.Combine(Path.GetTempPath(),
            "aegi-task-resolution-alias-" + Guid.NewGuid().ToString("N") + ".json"));
        await using var service = new AegiTaskService(resolver.ResolveAsync) { MaximumConcurrentTasks = 1 };
        var holderEntered = Gate();
        var holderRelease = Gate();
        var values = new ConcurrentQueue<int>();
        var holder = service.Submit(new TaskTestOperation
        {
            Execute = async _ =>
            {
                holderEntered.TrySetResult();
                await holderRelease.Task;
            }
        });
        await holderEntered.Task.WaitAsync(Timeout);
        TaskTestOperation Write(AegiTaskResource resource, int value, string? coalescingKey = "latest") => new(
            resources: [resource], coalescingKey: coalescingKey)
        {
            Execute = context =>
            {
                context.RequireResources([resource]);
                values.Enqueue(value);
                return Task.CompletedTask;
            }
        };
        var first = service.Submit(Write(declared, 1));
        try
        {
            await resolver.Entered.WaitAsync(Timeout);
            var intervening = boundary == "unknown boundary" ? service.Submit(Write(alias, 3, null)) : null;
            var replacement = service.Submit(Write(boundary == "different alias declaration" ? alias : declared, 2));
            if (boundary == "same declaration")
            {
                Assert.Same(first, replacement);
            }
            else
            {
                Assert.NotSame(first, replacement);
            }

            Assert.Equal(AegiTaskState.Queued, first.Snapshot.State);
            Assert.Empty(values);
            resolver.Complete(resolved);
            holderRelease.TrySetResult();
            await Task.WhenAll(holder.Completion, first.Completion, replacement.Completion,
                intervening?.Completion ?? Task.CompletedTask).WaitAsync(Timeout);
            int[] expected = boundary switch
            {
                "same declaration" => [2],
                "different alias declaration" => [1, 2],
                _ => UnknownBoundaryOrder
            };
            Assert.Equal(expected, values.ToArray());
        }
        finally
        {
            resolver.Complete(resolved);
            holderRelease.TrySetResult();
            first.RequestCancel();
        }
    }

    private static TaskCompletionSource Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static (AegiTaskResource Declared, AegiTaskResource Resolved) CreateStorageResources()
    {
        var path = Path.Combine(Path.GetTempPath(), "aegi-task-resolution-" + Guid.NewGuid().ToString("N") + ".json");
        return (AegiTaskResource.DeferredStoragePath(path), AegiTaskResource.StoragePath(path));
    }
}
