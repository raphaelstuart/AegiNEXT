using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Timing;

/// <summary>
/// 验证字幕区间的半开边界和非空约束。
/// </summary>
public sealed class MediaTimeRangeTests
{
    /// <summary>
    /// 相邻字幕在共同边界仅命中后一条。
    /// </summary>
    [Fact]
    public void AdjacentRangesDoNotBothContainSharedBoundary()
    {
        var first = new MediaTimeRange(new MediaTime(0), new MediaTime(1));
        var second = new MediaTimeRange(new MediaTime(1), new MediaTime(2));

        Assert.True(first.Contains(first.Start));
        Assert.False(first.Contains(first.End));
        Assert.True(second.Contains(first.End));
        Assert.False(first.Contains(new MediaTime(-1)));
        Assert.False(first.Overlaps(second));
    }

    /// <summary>
    /// 负媒体时间合法，且持续时间保持精确。
    /// </summary>
    [Fact]
    public void NegativeRangesRetainExactDurationAndOverlap()
    {
        var range = new MediaTimeRange(new MediaTime(-1001, 24000), new MediaTime(1001, 24000));
        var overlapping = new MediaTimeRange(MediaTime.Zero, new MediaTime(1));

        Assert.Equal(new MediaTime(1001, 12000), range.Duration);
        Assert.True(range.Contains(MediaTime.Zero));
        Assert.True(range.Overlaps(overlapping));
        Assert.True(overlapping.Overlaps(range));
    }

    /// <summary>
    /// 拒绝零长度和倒置区间。
    /// </summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    public void EmptyOrReversedRangesAreRejected(long start, long end)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTimeRange(new MediaTime(start), new MediaTime(end)));
    }

    /// <summary>
    /// 可表示的端点仍可能产生无法表示的持续时间，此时读取时明确溢出。
    /// </summary>
    [Fact]
    public void DurationOutsideRepresentationRangeThrows()
    {
        var range = new MediaTimeRange(new MediaTime(long.MinValue), new MediaTime(long.MaxValue));

        Assert.True(range.Contains(MediaTime.Zero));
        Assert.Throws<OverflowException>(() => range.Duration);
    }
}
