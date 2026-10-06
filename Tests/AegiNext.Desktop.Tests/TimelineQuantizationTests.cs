using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineQuantizationTests
{
    [Theory]
    [InlineData(48, 2, 2, 5)]
    [InlineData(100, 1, 1, 5)]
    [InlineData(1000, 0.1, 1, 50)]
    [InlineData(2000, 0.05, 1, 100)]
    [InlineData(20000, 0.005, 1, 1000)]
    public void MinorDivisionUsesTheSameScaleAsTheRuler(double pixelsPerSecond, double major, long numerator,
        long denominator)
    {
        Assert.Equal(major, TimelineTimeScale.MajorStep(pixelsPerSecond), 8);
        Assert.Equal(new MediaTime(numerator, denominator), TimelineTimeScale.MinorStep(pixelsPerSecond));
    }

    [Theory]
    [InlineData(125, 1000, 13, 100)]
    [InlineData(-125, 1000, -13, 100)]
    [InlineData(333, 1000, 33, 100)]
    [InlineData(334, 1000, 33, 100)]
    public void StepQuantizesAbsoluteTimeExactlyWithSymmetricHalfwayRounding(long numerator, long denominator,
        long expectedNumerator, long expectedDenominator)
    {
        Assert.Equal(new MediaTime(expectedNumerator, expectedDenominator),
            TimelineQuantization.Quantize(new(numerator, denominator), new(1, 100)));
    }

    [Fact]
    public void RationalFrameTimesQuantizeWithoutFloatingPointDrift()
    {
        Assert.Equal(new MediaTime(1001, 10), TimelineQuantization.Quantize(new(3003000, 30000), new(1, 100)));
        Assert.Throws<ArgumentOutOfRangeException>(() => TimelineQuantization.Quantize(new(1), MediaTime.Zero));
    }

    [Fact]
    public void SnapUsesPixelDistanceAndPreservesExactNonFrameBoundary()
    {
        var boundary = new MediaTime(1001, 1000);
        Assert.Equal(boundary, TimelineQuantization.Snap(new(1), [boundary], 2000));
        Assert.Equal(new MediaTime(49, 50), TimelineQuantization.Snap(new(49, 50), [boundary], 2000));
        Assert.Equal(new MediaTime(49, 50), TimelineQuantization.Snap(new(49, 50), [], 2000));
    }

    [Fact]
    public void WholeClipSnapChoosesClosestEdgeAndDoesNotMoveAnAlreadyAlignedClip()
    {
        Assert.Equal(new MediaTime(1, 100),
            TimelineQuantization.SnapOffset(new(1), new(3), [new(301, 100)], 100));
        Assert.Equal(new MediaTime(-1, 100),
            TimelineQuantization.SnapOffset(new(1), new(3), [new(99, 100), new(303, 100)], 100));
        Assert.Equal(MediaTime.Zero,
            TimelineQuantization.SnapOffset(new(1), new(3), [new(1), new(301, 100)], 100));
    }

    [Fact]
    public void SnapResultIncludesTheSameExactBoundaryUsedForTheEditIncludingZeroAdjustment()
    {
        var boundary = new MediaTime(301, 100);
        var snapped = TimelineQuantization.ResolveSnap(new(3), [boundary], 100);
        Assert.Equal(boundary, snapped.Value);
        Assert.Equal(boundary, snapped.Boundary);
        var whole = TimelineQuantization.ResolveSnapOffset(new(1), new(3), [boundary], 100);
        Assert.Equal(new MediaTime(1, 100), whole.Value);
        Assert.Equal(boundary, whole.Boundary);
        var aligned = TimelineQuantization.ResolveSnapOffset(new(1), boundary, [boundary], 100);
        Assert.Equal(MediaTime.Zero, aligned.Value);
        Assert.Equal(boundary, aligned.Boundary);
        Assert.Null(TimelineQuantization.ResolveSnap(new(2), [boundary], 100).Boundary);
        Assert.Null(TimelineQuantization.ResolveSnapOffset(new(1), new(2), [boundary], 100).Boundary);
    }

    [Theory]
    [InlineData(1.025, 0.05, "00:01.025")]
    [InlineData(61.001, 0.1, "01:01.001")]
    [InlineData(3601.125, 0.2, "01:00:01.125")]
    [InlineData(61, 2, "01:01")]
    public void SubsecondRulerLabelsShowMilliseconds(double seconds, double major, string expected)
    {
        Assert.Equal(expected, TimelineTimeScale.Label(seconds, major));
    }
}
