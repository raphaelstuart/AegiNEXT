using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisWorkerBudgetTests
{
    [Fact]
    public async Task SharedCapacityIsBoundedAndReleasingALeaseTwiceIsHarmless()
    {
        using var budget = new AudioAnalysisWorkerBudget(2, 8);
        var first = await budget.AcquireAsync(new());
        using var second = await budget.AcquireAsync(new());
        var thirdTask = budget.AcquireAsync(new()).AsTask();
        Assert.Equal(2, budget.ActiveWorkers);
        Assert.False(thirdTask.IsCompleted);
        first.Dispose();
        first.Dispose();
        using var third = await thirdTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, budget.ActiveWorkers);
    }

    [Fact]
    public async Task ForegroundHasPriorityButCannotStarveBackgroundOwners()
    {
        using var budget = new AudioAnalysisWorkerBudget(1, 8);
        var held = await budget.AcquireAsync(new());
        var background = budget.AcquireAsync(new()).AsTask();
        var foreground = Enumerable.Range(0, 5).Select(_ => budget.AcquireAsync(new(), true).AsTask()).ToArray();
        held.Dispose();
        for (var index = 0; index < 3; index++)
        {
            using var lease = await foreground[index].WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(background.IsCompleted);
        }
        using (await background.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.False(foreground[3].IsCompleted);
        }
        for (var index = 3; index < foreground.Length; index++)
        {
            using var lease = await foreground[index].WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task OwnersRotateWithinEachPriority()
    {
        using var budget = new AudioAnalysisWorkerBudget(1, 8);
        var ownerA = new object();
        var held = await budget.AcquireAsync(ownerA);
        var firstA = budget.AcquireAsync(ownerA).AsTask();
        var secondA = budget.AcquireAsync(ownerA).AsTask();
        var ownerB = budget.AcquireAsync(new()).AsTask();
        held.Dispose();
        using (await ownerB.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.False(firstA.IsCompleted);
        }
        using (await firstA.WaitAsync(TimeSpan.FromSeconds(5)))
        {
            Assert.False(secondA.IsCompleted);
        }
        using var last = await secondA.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task CancelledWaiterDoesNotConsumeCapacityOrBlockItsSuccessor()
    {
        using var budget = new AudioAnalysisWorkerBudget(1, 8);
        using var cancellation = new CancellationTokenSource();
        var held = await budget.AcquireAsync(new());
        var cancelled = budget.AcquireAsync(new(), cancellationToken: cancellation.Token).AsTask();
        var successor = budget.AcquireAsync(new()).AsTask();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        held.Dispose();
        using var lease = await successor.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, budget.ActiveWorkers);
    }

    [Fact]
    public async Task ReducingCapacityWaitsForExistingWorkersAndIncreasingItWakesWaiters()
    {
        using var budget = new AudioAnalysisWorkerBudget(3, 8);
        var first = await budget.AcquireAsync(new());
        var second = await budget.AcquireAsync(new());
        var third = await budget.AcquireAsync(new());
        budget.UpdateMaximumWorkers(1);
        var waiter = budget.AcquireAsync(new()).AsTask();
        first.Dispose();
        second.Dispose();
        Assert.False(waiter.IsCompleted);
        third.Dispose();
        using var lease = await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        var next = budget.AcquireAsync(new()).AsTask();
        budget.UpdateMaximumWorkers(2);
        using var nextLease = await next.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, budget.ActiveWorkers);
    }

    [Theory]
    [InlineData(1, 0, 1)]
    [InlineData(4, 0, 2)]
    [InlineData(16, 0, 4)]
    [InlineData(4, 20, 3)]
    public void WorkerLimitUsesPortableHardwareClamp(int processors, int requested, int expected)
    {
        using var budget = new AudioAnalysisWorkerBudget(requested, processors);
        Assert.Equal(expected, budget.MaximumWorkers);
    }

    [Fact]
    public async Task DisposalCancelsQueuedRequestsAndAllowsOutstandingLeaseRelease()
    {
        var budget = new AudioAnalysisWorkerBudget(1, 8);
        var lease = await budget.AcquireAsync(new());
        var waiter = budget.AcquireAsync(new()).AsTask();
        budget.Dispose();
        budget.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => budget.AcquireAsync(new()).AsTask());
        lease.Dispose();
        Assert.Equal(0, budget.ActiveWorkers);
    }
}
