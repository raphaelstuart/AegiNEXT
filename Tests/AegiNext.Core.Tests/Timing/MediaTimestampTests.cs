using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Timing;

/// <summary>
/// 验证原始媒体时间戳、时基和工程时间映射。
/// </summary>
public sealed class MediaTimestampTests
{
    /// <summary>
    /// 时基必须严格为正。
    /// </summary>
    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void NonPositiveTimeBasesAreRejected(long numerator, long denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTimeBase(numerator, denominator));
    }

    /// <summary>
    /// 相同时刻的不同源表示不应被合并为同一个原始时间戳。
    /// </summary>
    [Fact]
    public void TimestampRepresentationIsDistinctFromTimeEquality()
    {
        var video = new MediaTimestamp(90000, new MediaTimeBase(1, 90000));
        var audio = new MediaTimestamp(48000, new MediaTimeBase(1, 48000));

        Assert.NotEqual(video, audio);
        Assert.Equal(video.ToMediaTime(), audio.ToMediaTime());
        Assert.Equal(new MediaTimeBase(1, 90000), new MediaTimeBase(2, 180000));
        Assert.Equal(90000, video.Value);
    }

    /// <summary>
    /// long 最小值在领域层是有效整数，原生未定义标记由媒体适配器识别。
    /// </summary>
    [Fact]
    public void MinimumTimestampCanBeReducedWithoutIntermediateOverflow()
    {
        var timestamp = new MediaTimestamp(long.MinValue, new MediaTimeBase(3, 4));

        Assert.Equal(new MediaTime(long.MinValue / 4 * 3), timestamp.ToMediaTime());
    }

    /// <summary>
    /// VFR 帧必须使用真实 PTS，映射后保留不等帧间距。
    /// </summary>
    [Fact]
    public void VariableFrameRateMappingPreservesOriginalSpacing()
    {
        var timeBase = new MediaTimeBase(1, 1000);
        var source = new[] { 9000L, 9033L, 9083L, 9100L };
        var mapping = new MediaTimelineMapping(new MediaTimestamp(source[0], timeBase).ToMediaTime());
        var times = source.Select(value => mapping.ToProjectTime(new MediaTimestamp(value, timeBase).ToMediaTime()))
            .ToArray();

        Assert.Equal(new[] { MediaTime.Zero, new MediaTime(33, 1000), new MediaTime(83, 1000), new MediaTime(1, 10) },
            times);
        Assert.Equal(new MediaTime(33, 1000), times[1] - times[0]);
        Assert.Equal(new MediaTime(50, 1000), times[2] - times[1]);
        Assert.Equal(new MediaTime(17, 1000), times[3] - times[2]);

        for (var index = 0; index < source.Length; index++)
        {
            Assert.Equal(source[index],
                mapping.ToMediaTime(times[index]).ToTimestamp(timeBase, MediaTimeRounding.TO_EVEN).Value);
        }
    }

    /// <summary>
    /// 正负原点和零原点都应精确往返。
    /// </summary>
    [Theory]
    [InlineData(-7)]
    [InlineData(0)]
    [InlineData(7)]
    public void TimelineMappingRoundTripsAcrossOrigins(long origin)
    {
        var mapping = new MediaTimelineMapping(new MediaTime(origin, 3));
        var time = new MediaTime(-123456789, 90000);

        Assert.Equal(time, mapping.ToMediaTime(mapping.ToProjectTime(time)));
        Assert.Equal(time, default(MediaTimelineMapping).ToProjectTime(time));
    }

    /// <summary>
    /// 映射原点归零时允许数学中间值大于 long。
    /// </summary>
    [Fact]
    public void MinimumOriginMapsToZero()
    {
        var origin = new MediaTime(long.MinValue);

        Assert.Equal(MediaTime.Zero, new MediaTimelineMapping(origin).ToProjectTime(origin));
    }

    /// <summary>
    /// 从时间重新量化必须显式选择目标时基与取整策略。
    /// </summary>
    [Fact]
    public void TimestampQuantizationUsesTargetTimeBase()
    {
        var time = new MediaTime(1, 24);
        var timeBase = new MediaTimeBase(1, 1000);

        Assert.Equal(41, time.ToTimestamp(timeBase, MediaTimeRounding.FLOOR).Value);
        Assert.Equal(42, time.ToTimestamp(timeBase, MediaTimeRounding.CEILING).Value);
        Assert.Equal(timeBase, time.ToTimestamp(timeBase, MediaTimeRounding.TO_EVEN).TimeBase);
    }

    /// <summary>
    /// 未提供时基不能被解释成零时间或隐式秒单位。
    /// </summary>
    [Fact]
    public void MissingTimeBaseIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new MediaTimestamp(0, null!));
        Assert.Throws<ArgumentNullException>(() => MediaTime.Zero.ToTimestamp(null!, MediaTimeRounding.TO_EVEN));
    }
}
