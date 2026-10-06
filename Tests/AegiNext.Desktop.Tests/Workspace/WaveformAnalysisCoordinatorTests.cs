using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Workspace;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Tests.Workspace;

public sealed class WaveformAnalysisCoordinatorTests
{
    [Fact]
    public async Task NewViewportReplacesLateResultAndCacheAvoidsDecodingOnSmallPan()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new List<WaveformAnalysisRequest>();
        WaveformData? shown = null;
        using var coordinator = new WaveformAnalysisCoordinator((detail, _, _) => shown = detail, _ => { }, TimeSpan.Zero);
        await coordinator.StartAsync(async (request, _) =>
        {
            calls.Add(request);
            if (calls.Count == 1)
            {
                entered.SetResult();
                await release.Task;
            }
            return Data(request);
        }, new(21600));
        var firstViewport = new TimelineViewport(10, 480, Width: 800);
        coordinator.Request(firstViewport, 1, true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latestViewport = firstViewport with { StartSeconds = 100 };
        coordinator.Request(latestViewport, 1, true);
        release.SetResult();
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(shown);
        var visible = WaveformViewportPlanner.Create(latestViewport, 1, new(21600))!.Visible;
        Assert.True(shown.Start <= visible.Start && shown.End >= visible.End);
        var callsBeforePan = calls.Count;
        coordinator.Request(latestViewport with { StartSeconds = 100.1 }, 1, true);
        await coordinator.Completion;
        Assert.Equal(callsBeforePan, calls.Count);
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task ClearWaitsForWorkAndRejectsOldMediaResults()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        WaveformData? shown = null;
        MediaTime shownDuration = new(1);
        using var coordinator = new WaveformAnalysisCoordinator((detail, _, duration) =>
        {
            shown = detail;
            shownDuration = duration;
        }, _ => { }, TimeSpan.Zero);
        await coordinator.StartAsync(async (request, _) =>
        {
            entered.TrySetResult();
            await release.Task;
            return Data(request);
        }, new(100));
        coordinator.Request(new(10, 480, Width: 800), 1, true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var clearing = coordinator.ClearAsync();
        Assert.False(clearing.IsCompleted);
        release.SetResult();
        await clearing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Null(shown);
        Assert.Equal(MediaTime.Zero, shownDuration);
        await coordinator.StartAsync((request, _) => Task.FromResult(Data(request)), new(200));
        coordinator.Request(new(150, 480, Width: 800), 1, true);
        await coordinator.Completion;
        Assert.NotNull(shown);
        Assert.Equal(new MediaTime(200), shownDuration);
        Assert.True(shown.Start > new MediaTime(100));
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task HiddenWaveformCancelsWorkAndRestoresFromCache()
    {
        var calls = 0;
        WaveformData? shown = null;
        using var coordinator = new WaveformAnalysisCoordinator((detail, _, _) => shown = detail, _ => { }, TimeSpan.Zero);
        await coordinator.StartAsync((request, _) =>
        {
            calls++;
            return Task.FromResult(Data(request));
        }, new(100));
        var viewport = new TimelineViewport(10, 480, Width: 800);
        coordinator.Request(viewport, 1, true);
        await coordinator.Completion;
        var original = shown;
        coordinator.Request(viewport, 1, false);
        await coordinator.Completion;
        Assert.Null(shown);
        var beforeRestore = calls;
        coordinator.Request(viewport, 1, true);
        await coordinator.Completion;
        Assert.Same(original, shown);
        Assert.Equal(beforeRestore, calls);
        await coordinator.ClearAsync();
    }

    [Fact]
    public async Task FailingRangeReportsOnceAndLaterNavigationCanRecover()
    {
        var failures = 0;
        WaveformData? shown = null;
        using var coordinator = new WaveformAnalysisCoordinator((detail, _, _) => shown = detail, _ => failures++, TimeSpan.Zero);
        await coordinator.StartAsync((_, _) => Task.FromException<WaveformData>(new InvalidDataException("decode")), new(100));
        coordinator.Request(new(10, 480, Width: 800), 1, true);
        await coordinator.Completion;
        Assert.Equal(1, failures);
        Assert.Null(shown);
        await coordinator.StartAsync((request, _) => Task.FromResult(Data(request)), new(100));
        coordinator.Request(new(20, 480, Width: 800), 1, true);
        await coordinator.Completion;
        Assert.NotNull(shown);
        await coordinator.ClearAsync();
    }

    [Fact]
    public void CacheEvictsOldestDataByPayloadBudgetAndRefreshesHits()
    {
        var cache = new WaveformCache(32);
        var first = Data(new(MediaTime.Zero, 1, 2));
        var second = Data(new(new(4, 48000), 1, 2));
        var third = Data(new(new(8, 48000), 1, 2));
        cache.Add(first);
        cache.Add(second);
        Assert.Same(first, cache.Find(first.Request));
        cache.Add(third);
        Assert.Null(cache.Find(second.Request));
        Assert.Same(first, cache.Find(first.Request));
        Assert.Same(third, cache.Find(third.Request));
        Assert.Equal(32, cache.MemoryBytes);
        cache.Clear();
        Assert.Equal(0, cache.MemoryBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SynchronousPublicationRegistersCompletionBeforeReentrantClear(bool clearFromOverview)
    {
        WaveformAnalysisCoordinator? coordinator = null;
        Task? clearing = null;
        var completedInsidePublication = false;
        using var owned = new WaveformAnalysisCoordinator((detail, overview, _) =>
        {
            if (clearing is null && (clearFromOverview ? overview is not null : detail is not null))
            {
                clearing = coordinator!.ClearAsync();
                completedInsidePublication = clearing.IsCompleted;
            }
        }, _ => { }, TimeSpan.Zero);
        coordinator = owned;
        await coordinator.StartAsync((request, _) => Task.FromResult(Data(request)), new(100));
        coordinator.Request(new(10, 480, Width: 800), 1, true);
        await coordinator.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotNull(clearing);
        await clearing.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(completedInsidePublication);
    }

    [Fact]
    public async Task RapidHideAndShowRejectsCanceledOverviewAndWaitsForItsReplacement()
    {
        var firstEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirst = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseSecond = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publishedOverview = new List<WaveformData>();
        var overviewCalls = 0;
        using var coordinator = new WaveformAnalysisCoordinator((_, overview, _) =>
        {
            if (overview is not null)
            {
                publishedOverview.Add(overview);
            }
        }, _ => { }, TimeSpan.Zero);
        var overviewRequest = WaveformViewportPlanner.CreateOverview(new(100));
        await coordinator.StartAsync(async (request, _) =>
        {
            if (request == overviewRequest)
            {
                overviewCalls++;
                if (overviewCalls == 1)
                {
                    firstEntered.SetResult();
                    await releaseFirst.Task;
                }
                else
                {
                    secondEntered.SetResult();
                    await releaseSecond.Task;
                }
            }
            return Data(request);
        }, new(100));
        var viewport = new TimelineViewport(10, 480, Width: 800);
        coordinator.Request(viewport, 1, true);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        coordinator.Request(viewport, 1, false);
        coordinator.Request(viewport, 1, true);
        var completion = coordinator.Completion;
        releaseFirst.SetResult();
        await secondEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Empty(publishedOverview);
        Assert.False(completion.IsCompleted);
        releaseSecond.SetResult();
        await completion.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(publishedOverview);
        Assert.Equal(2, overviewCalls);
        await coordinator.ClearAsync();
    }

    private static WaveformData Data(WaveformAnalysisRequest request)
    {
        return new(request, new float[request.BucketCount * 2]);
    }
}
