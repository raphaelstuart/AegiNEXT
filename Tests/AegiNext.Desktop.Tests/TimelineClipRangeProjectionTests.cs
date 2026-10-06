using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineClipRangeProjectionTests
{
    [Fact]
    public void OverlappingTracksPartitionOnceWithSelectedAndInvalidPriority()
    {
        TimelineClipRange[] ranges =
        [
            new(new(1), new(8), false, false),
            new(new(2), new(5), true, false),
            new(new(3), new(6), true, false),
            new(new(4), new(7), true, true),
            new(new(1), new(8), false, false)
        ];
        Assert.Equal(new TimelineClipRange[]
        {
            new(new(1), new(2), false, false),
            new(new(2), new(4), true, false),
            new(new(4), new(7), true, true),
            new(new(7), new(8), false, false)
        }, TimelineClipRangeProjection.Partition(ranges));
        Assert.Equal(TimelineClipRangeProjection.Partition(ranges), TimelineClipRangeProjection.Partition(ranges.Reverse()));
    }

    [Fact]
    public void CoincidentBoundariesMergeAdjacentRangesAndLeaveGapsEmpty()
    {
        var result = TimelineClipRangeProjection.Partition(
        [
            new(new(1, 3), new(2, 3), true, false),
            new(new(2, 3), new(1), true, false),
            new(new(1), new(1), false, false),
            new(new(2), new(3), false, false)
        ]);
        Assert.Equal(new TimelineClipRange[]
        {
            new(new(1, 3), new(1), true, false),
            new(new(2), new(3), false, false)
        }, result);
        Assert.All(result, range => Assert.True(range.End > range.Start));
    }
}
