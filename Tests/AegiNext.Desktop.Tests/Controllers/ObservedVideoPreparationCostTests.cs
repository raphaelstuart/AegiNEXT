using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;

namespace AegiNext.Desktop.Tests.Controllers;

/// <summary>播放调度成本以有限精度记录，避免连续反馈扩大时间分母。</summary>
public sealed class ObservedVideoPreparationCostTests
{
    /// <summary>成本向上舍入到时钟刻度，保留观测时间和正值。</summary>
    [Theory]
    [InlineData(1, 3, 3333334)]
    [InlineData(922337203685477581, long.MaxValue, 1000001)]
    [InlineData(1, long.MaxValue, 1)]
    [InlineData(0, long.MaxValue, 0)]
    public void SchedulingMeasurementsUseExplicitCeilingPrecisionWithoutChangingTheirTimestamp(
        long numerator, long denominator, long expectedTicks)
    {
        var observation = new ObservedVideoPreparationCost(new(numerator, denominator), 12345);

        Assert.Equal(MediaTime.FromTimeSpan(TimeSpan.FromTicks(expectedTicks)), observation.Cost);
        Assert.Equal(12345, observation.Timestamp);
        Assert.Equal(0, TimeSpan.TicksPerSecond % observation.Cost.Denominator);
    }

    /// <summary>毫秒量级的大分母成本可安全参与调度相加。</summary>
    [Fact]
    public void SmallCostsWithLargeDenominatorsCanBeCombinedForScheduling()
    {
        var expected = new ObservedVideoPreparationCost(new(1, 10), 0);
        var wake = new ObservedVideoPreparationCost(new(922337203685477581, long.MaxValue), 1);

        var lead = expected.Cost + wake.Cost;

        Assert.Equal(MediaTime.FromTimeSpan(TimeSpan.FromTicks(2000001)), lead);
    }

    /// <summary>常见整数及 NTSC 帧率的长期唤醒反馈保持可表示。</summary>
    [Theory]
    [InlineData(60, 1)]
    [InlineData(30000, 1001)]
    [InlineData(24000, 1001)]
    public void TwoThousandWakeFeedbackObservationsRemainRepresentableAtFractionalFrameRates(
        long frameRateNumerator, long frameRateDenominator)
    {
        var frameDuration = new MediaTime(frameRateDenominator, frameRateNumerator);
        var expected = new ObservedVideoPreparationCost(new(200001, 20000000), 0).Cost;
        var observations = new Queue<ObservedVideoPreparationCost>();
        observations.Enqueue(new(MediaTime.Zero, 0));
        for (var frame = 1; frame <= 2000; frame++)
        {
            var wake = (observations.Min(value => value.Cost) + observations.Max(value => value.Cost)) / 2;
            var bias = frameDuration / 2;
            bias = bias < expected ? bias : expected;
            var target = frameDuration * frame + bias - expected - wake;
            var actualWakeTicks = target.ToTimestamp(MediaTimeBase.TimeSpanTicks, MediaTimeRounding.CEILING).Value + 133;
            var actualWake = MediaTime.FromTimeSpan(TimeSpan.FromTicks(actualWakeTicks));
            observations.Enqueue(new(actualWake - target, frame));
            if (observations.Count > 16)
            {
                observations.Dequeue();
            }
        }

        Assert.All(observations, value =>
        {
            Assert.Equal(0, TimeSpan.TicksPerSecond % value.Cost.Denominator);
            Assert.True(value.Cost >= MediaTime.Zero);
        });
    }
}
