using AegiNext.Core.Media;

namespace AegiNext.Core.Tests.Media;

public class MediaRatioTests
{
    [Theory]
    [InlineData(2, 4, 1, 2)]
    [InlineData(-2, 4, -1, 2)]
    [InlineData(0, long.MaxValue, 0, 1)]
    [InlineData(long.MinValue, 1, long.MinValue, 1)]
    [InlineData(long.MinValue, 2, long.MinValue / 2, 1)]
    [InlineData(long.MaxValue, long.MaxValue, 1, 1)]
    public void NormalizesExactComponentsWithoutOverflow(long numerator, long denominator,
        long expectedNumerator, long expectedDenominator)
    {
        var ratio = new MediaRatio(numerator, denominator);

        Assert.Equal(expectedNumerator, ratio.Numerator);
        Assert.Equal(expectedDenominator, ratio.Denominator);
        Assert.Equal(new MediaRatio(expectedNumerator, expectedDenominator), ratio);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void RejectsNonPositiveDenominators(long denominator)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => new MediaRatio(1, denominator));

        Assert.Equal("denominator", exception.ParamName);
    }
}
