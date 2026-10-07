using AegiNext.Media.Playback;
using AegiNext.Media.Tests.Decoding;

namespace AegiNext.Media.Tests.Playback;

public sealed class SynchronousMediaWorkerTests
{
    [Fact]
    public async Task ResourceOperationsStayOnOneDedicatedThreadAndRejectReentrancy()
    {
        await using var worker = new SynchronousMediaWorker("Media worker ownership test");
        var firstThread = await worker.ExecuteAsync(() =>
        {
            Assert.False(Thread.CurrentThread.IsThreadPoolThread);
            return Environment.CurrentManagedThreadId;
        });
        var secondThread = await worker.ExecuteAsync(() => Environment.CurrentManagedThreadId);
        Assert.Equal(firstThread, secondThread);
        await worker.ExecuteAsync(() =>
        {
            var reentrant = worker.ExecuteAsync(() => 1);
            Assert.Throws<InvalidOperationException>(() => reentrant.GetAwaiter().GetResult());
        });
    }

    [Fact]
    public async Task ClosingDrainsPendingJobsAndAwaitsTheRunningOwnedResult()
    {
        var worker = new SynchronousMediaWorker("Media worker drain test");
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = worker.ExecuteAsync(() =>
        {
            entered.TrySetResult();
            release.Wait();
            return new FakeVideoFrame(0, 1);
        }, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var pending = worker.ExecuteAsync(() => throw new InvalidOperationException("A closed pending job must not execute."));
        var waitingForRoom = worker.ExecuteAsync(() => throw new InvalidOperationException("A closed enqueue must not execute."));
        await cancellation.CancelAsync();
        var closing = worker.DisposeAsync().AsTask();
        Assert.False(closing.IsCompleted);
        Assert.False(running.IsCompleted);
        release.Set();
        var ownedResult = await running.WaitAsync(TimeSpan.FromSeconds(5));
        ownedResult.Dispose();
        Assert.Equal(1, ownedResult.DisposeCount);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => waitingForRoom.WaitAsync(TimeSpan.FromSeconds(5)));
        await closing.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => worker.ExecuteAsync(() => 1));
    }
}
