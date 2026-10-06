using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests;

public sealed class ScenePreviewSchedulerTests
{
    [Fact]
    public async Task BlockedCompositionKeepsOnlyTheNewestWaitingTargetAndNeverPresentsAnObsoleteResult()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var presented = new TaskCompletionSource<ScenePreviewResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var composed = new List<long>();
        using var scheduler = new ScenePreviewScheduler((request, token) =>
        {
            composed.Add(request.Sequence);
            if (request.Sequence == 1)
            {
                entered.SetResult();
                Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
            }
            token.ThrowIfCancellationRequested();
            return new(1, 1, new byte[] { 0, 0, 0, 255 });
        }, result => presented.TrySetResult(result));
        var document = new ProjectDocument();
        ScenePreviewRequest Request(long sequence, bool interactive) => new(sequence, 1, document, new(sequence), new(0), null, 1, 1, Path.GetTempPath(), interactive);
        scheduler.Submit(Request(1, true));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Submit(Request(2, true));
        scheduler.Submit(Request(3, false));
        unblock.Set();
        var result = await presented.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new long[] { 1, 3 }, composed);
        Assert.Equal(3, result.Request.Sequence);
        Assert.False(result.Request.Interactive);
        Assert.Equal(new MediaTime(3), result.Request.TargetTime);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task DisposalWhileCompositionIsBlockedSuppressesPresentation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var presentCount = 0;
        using var scheduler = new ScenePreviewScheduler((_, token) =>
        {
            entered.SetResult();
            try
            {
                Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
                token.ThrowIfCancellationRequested();
                return new(1, 1, new byte[] { 0, 0, 0, 255 });
            }
            finally
            {
                exited.SetResult();
            }
        }, _ => Interlocked.Increment(ref presentCount));
        scheduler.Submit(new(1, 1, new(), MediaTime.Zero, null, null, 1, 1, Path.GetTempPath(), true));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Dispose();
        unblock.Set();
        await exited.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, presentCount);
    }
}
