using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineVisibleClipIndexTests
{
    [Fact]
    public void QueryPreservesDrawingOrderAndIncludesLongOverlappingClips()
    {
        ProjectLayer[] clips =
        [
            new() { Start = new(8), End = new(12) },
            new() { Start = MediaTime.Zero, End = new(100) },
            new() { Start = new(9), End = new(11) },
            new() { Start = new(11), End = new(13) },
            new() { Start = new(5), End = new(9) }
        ];
        var index = new TimelineVisibleClipIndex(clips);

        Assert.Equal(new[] { clips[0], clips[1], clips[2] }, index.Query(new(9), new(11)));
        Assert.Empty(index.Query(new(100), new(101)));
    }

    [Fact]
    public void ALongEarlyClipDoesNotForceScanningAllEarlierShortClips()
    {
        var clips = Enumerable.Range(0, 8192).Select(value => new ProjectLayer
        {
            Start = new(value * 2), End = new(value * 2 + 1)
        }).ToArray();
        clips[0] = clips[0] with { End = new(20000) };
        var index = new TimelineVisibleClipIndex(clips);

        Assert.Equal(new[] { clips[0], clips[7000] }, index.Query(new(14000), new(14001)));
        Assert.InRange(index.LastVisitedNodeCount, 1, 100);
    }

    [Fact]
    public void LargeHighDpiDrawingFallsBackBeforeExceedingTheControlBudget()
    {
        Assert.True(TimelineDrawingCache.CanCache(new Size(800, 260), 2, 5));
        Assert.False(TimelineDrawingCache.CanCache(new Size(4096, 4096), 2, 5));
        Assert.False(TimelineDrawingCache.CanCache(new Size(0, 260), 1, 5));
        Assert.False(TimelineDrawingCache.CanCache(new Size(800, 260), double.PositiveInfinity, 5));
    }

    [Fact]
    public void EmptyAndZeroWidthQueriesReturnNoClips()
    {
        var index = new TimelineVisibleClipIndex([]);
        Assert.Empty(index.Query(MediaTime.Zero, new(1)));
        Assert.Empty(new TimelineVisibleClipIndex([new() { End = new(2) }]).Query(new(1), new(1)));
    }
}
