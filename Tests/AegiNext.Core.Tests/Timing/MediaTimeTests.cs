using AegiNext.Core.Timing;

namespace AegiNext.Core.Tests.Timing;

/// <summary>
/// 验证精确时间运算、默认值和有限表示范围。
/// </summary>
public sealed class MediaTimeTests
{
    /// <summary>
    /// 等值分数与默认零必须具有相同的相等和哈希行为。
    /// </summary>
    [Fact]
    public void EquivalentFractionsHaveCanonicalEquality()
    {
        Assert.Equal(new MediaTime(1, 2), new MediaTime(2, 4));
        Assert.Equal(new MediaTime(-1, 2), new MediaTime(-2, 4));
        Assert.Equal(new MediaTime(1, 2).GetHashCode(), new MediaTime(2, 4).GetHashCode());
        Assert.Equal(MediaTime.Zero, default);
        Assert.Equal(default, new MediaTime(0, long.MaxValue));
        Assert.Equal(1, default(MediaTime).Denominator);
        Assert.Equal(new MediaTime(0, 1).GetHashCode(), default(MediaTime).GetHashCode());
    }

    /// <summary>
    /// 构造时拒绝非正分母。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void NonPositiveDenominatorsAreRejected(long denominator)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTime(1, denominator));
    }

    /// <summary>
    /// 分数帧率通过整数运算积累后仍保持精确。
    /// </summary>
    [Theory]
    [InlineData(24000)]
    [InlineData(30000)]
    public void FractionalFrameDurationsDoNotAccumulateDrift(long frameRateNumerator)
    {
        var frameDuration = new MediaTime(1001, frameRateNumerator);
        var total = MediaTime.Zero;

        for (var frame = 0; frame < frameRateNumerator; frame++)
        {
            total += frameDuration;
        }

        Assert.Equal(new MediaTime(1001), total);
        Assert.Equal(new MediaTime(1001 * 60 * 60), frameDuration * (frameRateNumerator * 60 * 60));
    }

    /// <summary>
    /// 中间整数超出 long 时，仍应先约分再判断最终结果。
    /// </summary>
    [Fact]
    public void LargeIntermediateValuesAreReducedBeforeOverflowChecks()
    {
        var nearOne = new MediaTime(long.MaxValue - 1, long.MaxValue);

        Assert.Equal(new MediaTime(1), nearOne + new MediaTime(1, long.MaxValue));
        Assert.Equal(new MediaTime(long.MaxValue - 1), nearOne * long.MaxValue);
        Assert.Equal(new MediaTime(1), new MediaTime(long.MinValue) / long.MinValue);
        Assert.Equal(MediaTime.Zero, new MediaTime(long.MinValue) - new MediaTime(long.MinValue));
        Assert.Equal(new MediaTime(long.MinValue / 2), new MediaTime(long.MinValue, 2));
    }

    /// <summary>
    /// 比较不得丢失浮点数无法表达的相邻时间差。
    /// </summary>
    [Fact]
    public void OrderingPreservesDifferencesBeyondDoublePrecision()
    {
        var earlier = new MediaTime(long.MaxValue - 1, long.MaxValue);
        var later = new MediaTime(1);

        Assert.True(earlier < later);
        Assert.True(later > earlier);
        Assert.True(earlier <= later);
        Assert.True(later >= earlier);
        Assert.True(new MediaTime(long.MinValue) < new MediaTime(long.MaxValue));
        Assert.Equal(0, earlier.CompareTo(new MediaTime(long.MaxValue - 1, long.MaxValue)));
    }

    /// <summary>
    /// 有限表示域之外的结果必须明确抛出溢出异常。
    /// </summary>
    [Fact]
    public void UnrepresentableResultsThrowOverflow()
    {
        Assert.Throws<OverflowException>(() => new MediaTime(long.MaxValue) + new MediaTime(1));
        Assert.Throws<OverflowException>(() => -new MediaTime(long.MinValue));
        Assert.Throws<OverflowException>(() => new MediaTime(1, long.MaxValue) / 2);
        Assert.Throws<OverflowException>(() => new MediaTime(long.MaxValue).ToTimeSpan(MediaTimeRounding.TO_EVEN));
        Assert.Throws<DivideByZeroException>(() => new MediaTime(1) / 0);
    }

    /// <summary>
    /// TimeSpan 的两个极值也必须精确往返。
    /// </summary>
    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void TimeSpanRoundTripsExactly(long ticks)
    {
        var source = TimeSpan.FromTicks(ticks);

        Assert.Equal(source, MediaTime.FromTimeSpan(source).ToTimeSpan(MediaTimeRounding.TO_EVEN));
    }

    /// <summary>
    /// 正负中点及非中点时间均遵循调用者指定的量化规则。
    /// </summary>
    [Theory]
    [InlineData(1, 2, MediaTimeRounding.TO_EVEN, 0)]
    [InlineData(3, 2, MediaTimeRounding.TO_EVEN, 2)]
    [InlineData(5, 2, MediaTimeRounding.TO_EVEN, 2)]
    [InlineData(-1, 2, MediaTimeRounding.TO_EVEN, 0)]
    [InlineData(-3, 2, MediaTimeRounding.TO_EVEN, -2)]
    [InlineData(-5, 2, MediaTimeRounding.TO_EVEN, -2)]
    [InlineData(8, 3, MediaTimeRounding.TO_EVEN, 3)]
    [InlineData(-8, 3, MediaTimeRounding.TO_EVEN, -3)]
    [InlineData(1, 3, MediaTimeRounding.TO_EVEN, 0)]
    [InlineData(-1, 3, MediaTimeRounding.TO_EVEN, 0)]
    [InlineData(4, 3, MediaTimeRounding.TO_EVEN, 1)]
    [InlineData(-4, 3, MediaTimeRounding.TO_EVEN, -1)]
    [InlineData(5, 2, MediaTimeRounding.TOWARD_ZERO, 2)]
    [InlineData(-5, 2, MediaTimeRounding.TOWARD_ZERO, -2)]
    [InlineData(5, 2, MediaTimeRounding.FLOOR, 2)]
    [InlineData(-5, 2, MediaTimeRounding.FLOOR, -3)]
    [InlineData(5, 2, MediaTimeRounding.CEILING, 3)]
    [InlineData(-5, 2, MediaTimeRounding.CEILING, -2)]
    public void QuantizationUsesExplicitRounding(long numerator, long denominator, MediaTimeRounding rounding,
        long expected)
    {
        var time = new MediaTime(numerator, denominator * TimeSpan.TicksPerSecond);

        Assert.Equal(expected, time.ToTimeSpan(rounding).Ticks);
    }

    /// <summary>
    /// 即使时间正好落在整数刻度，也必须拒绝未知取整策略。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UnknownRoundingIsRejectedBeforeExactDivision(long seconds)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediaTime(seconds).ToTimeSpan((MediaTimeRounding)99));
    }

    /// <summary>
    /// 仅在执行取整之后检查最终时间戳是否超出 long 范围。
    /// </summary>
    [Fact]
    public void QuantizationChecksOverflowAfterRounding()
    {
        var time = new MediaTime(6148914691236517205, 2);
        var timeBase = new MediaTimeBase(1, 3);

        Assert.Equal(long.MaxValue, time.ToTimestamp(timeBase, MediaTimeRounding.TOWARD_ZERO).Value);
        Assert.Equal(long.MaxValue, time.ToTimestamp(timeBase, MediaTimeRounding.FLOOR).Value);
        Assert.Throws<OverflowException>(() => time.ToTimestamp(timeBase, MediaTimeRounding.TO_EVEN));
        Assert.Throws<OverflowException>(() => time.ToTimestamp(timeBase, MediaTimeRounding.CEILING));
    }

    /// <summary>
    /// 负除数和零仍应保持规范化，并明确处理最小值取反溢出。
    /// </summary>
    [Fact]
    public void NegativeDivisorsPreserveCanonicalValues()
    {
        Assert.Equal(new MediaTime(3, 4), new MediaTime(-3, 2) / -2);
        Assert.Equal(MediaTime.Zero, MediaTime.Zero / long.MinValue);
        Assert.Throws<OverflowException>(() => new MediaTime(long.MinValue) / -1);
    }
}
