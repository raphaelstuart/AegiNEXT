using AegiNext.Core.Presets;
using AegiNext.Desktop.Rendering;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleStylePreviewSchedulerTests
{
    [Fact]
    public async Task RapidRequestsAreSerializedAndOnlyTheNewestWaitingSampleIsRendered()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var composed = new List<long>();
        var results = new List<SubtitleStylePreviewResult>();
        using var scheduler = new SubtitleStylePreviewScheduler((request, token) =>
        {
            composed.Add(request.Revision);
            if (request.Revision == 1)
            {
                entered.SetResult();
                Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
            }
            token.ThrowIfCancellationRequested();
            return new(1, 1, [0, 0, 0, 255]);
        }, result => results.Add(result));
        scheduler.Submit(Request(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        scheduler.Submit(Request(2));
        scheduler.Submit(Request(3));
        unblock.Set();
        await scheduler.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new long[] { 1, 3 }, composed);
        Assert.Equal(3, Assert.Single(results).Request.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidatingOrClosingWhileRenderingSuppressesLateResults(bool dispose)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var unblock = new ManualResetEventSlim();
        var results = 0;
        using var scheduler = new SubtitleStylePreviewScheduler((_, _) =>
        {
            entered.SetResult();
            Assert.True(unblock.Wait(TimeSpan.FromSeconds(5), CancellationToken.None));
            return new SdrVideoFrame(1, 1, [0, 0, 0, 255]);
        }, _ => Interlocked.Increment(ref results));
        scheduler.Submit(Request(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var completion = scheduler.Completion;
        if (dispose)
        {
            scheduler.Dispose();
        }
        else
        {
            scheduler.Invalidate(2);
        }
        unblock.Set();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, results);
    }

    private static SubtitleStylePreviewRequest Request(long revision)
    {
        return new(revision, new SubtitleStylePreset(Guid.NewGuid(), "Example", new()), "ABC", 320, 180, new());
    }
}
