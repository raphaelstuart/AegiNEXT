using System.Globalization;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineTimeTextTests
{
    [Theory]
    [InlineData("00:00:01.001", 1001, 1000)]
    [InlineData("-00:00:01.001", -1001, 1000)]
    [InlineData("  -01:02:03.125  ", -29785, 8)]
    [InlineData("90:01.5", 10803, 2)]
    [InlineData(".000000001", 1, 1000000000)]
    [InlineData("1.000000000000000000000000000", 1, 1)]
    [InlineData("-9223372036854775808", long.MinValue, 1)]
    [InlineData("9223372036854775807", long.MaxValue, 1)]
    public void ParsingPreservesExactDecimalTimeAndSign(string text, long numerator, long denominator)
    {
        Assert.Equal(new MediaTime(numerator, denominator), TimelineTimeText.Parse(text));
    }

    [Theory]
    [InlineData(1, 3, "00:00:00.333")]
    [InlineData(-1, 3, "-00:00:00.334")]
    [InlineData(-1, 3000, "-00:00:00.001")]
    [InlineData(1, 3000, "00:00:00.000")]
    [InlineData(-1001, 1000, "-00:00:01.001")]
    [InlineData(3600001, 1000, "01:00:00.001")]
    public void FormattingFloorsDirectlyToMilliseconds(long numerator, long denominator, string expected)
    {
        Assert.Equal(expected, TimelineTimeText.Format(new(numerator, denominator)));
    }

    [Theory]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void LargeTimesDoNotOverflowTimeSpanOrLoseSign(long seconds)
    {
        var original = new MediaTime(seconds);
        Assert.Equal(original, TimelineTimeText.Parse(TimelineTimeText.Format(original)));
    }

    [Theory]
    [InlineData("1:60")]
    [InlineData("1:60:00")]
    [InlineData("1::00")]
    [InlineData("1:2:3:4")]
    [InlineData("1e3")]
    [InlineData("1,5")]
    [InlineData("NaN")]
    [InlineData("+1")]
    [InlineData("1.-5")]
    [InlineData("--1")]
    [InlineData(".")]
    [InlineData("1.2.3")]
    [InlineData("١")]
    [InlineData("9223372036854775808")]
    [InlineData("0.0000000000000000001")]
    public void MalformedOrUnrepresentableInputFailsWithoutQuantization(string text)
    {
        Assert.Throws<FormatException>(() => TimelineTimeText.Parse(text));
    }

    [Fact]
    public void TimeTextIsIndependentOfOperatingSystemCulture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(new MediaTime(5, 4), TimelineTimeText.Parse("1.25"));
            Assert.Equal("00:00:01.250", TimelineTimeText.Format(new(5, 4)));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
