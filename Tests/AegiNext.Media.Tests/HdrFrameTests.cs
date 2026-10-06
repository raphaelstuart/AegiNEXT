using AegiNext.Rendering;

namespace AegiNext.Media.Tests;

public class HdrFrameTests
{
    [Fact]
    public void PreservesNonSquareDimensionsRgbaOrderAndLuminanceMetadata()
    {
        var info = new RenderSurfaceInfo(3, 2, 100);
        var pixels = new[]
        {
            (Half)1, (Half)0, (Half)0, (Half)1,
            (Half)0, (Half)1, (Half)0, (Half)1,
            (Half)0, (Half)0, (Half)1, (Half)1,
            (Half)0.25f, (Half)0.5f, (Half)0.75f, (Half)1,
            (Half)2, (Half)(-0.25f), (Half)1, (Half)0.5f,
            (Half)0, (Half)0, (Half)0, (Half)0
        };

        var frame = new HdrFrame(info, pixels, 1000);

        Assert.Same(info, frame.Info);
        Assert.Equal(3, frame.Info.Width);
        Assert.Equal(2, frame.Info.Height);
        Assert.Equal(24, frame.Info.RowBytes);
        Assert.Equal(48, frame.Info.ByteCount);
        Assert.Equal(24, frame.Pixels.Length);
        Assert.Equal(100f, frame.Info.ReferenceWhiteNits);
        Assert.Equal(1000f, frame.SourcePeakNits);
        Assert.Equal(pixels, frame.Pixels.ToArray());
    }

    [Fact]
    public void CopiesInputStorageBeforeCallerReusesIt()
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)2, (Half)0.5f, (Half)0.25f, (Half)0.5f };
        var expected = pixels.ToArray();
        var firstFrame = new HdrFrame(info, pixels, 1000);

        Array.Fill(pixels, (Half)0);
        var secondFrame = new HdrFrame(info, pixels, 400);

        Assert.Equal(expected, firstFrame.Pixels.ToArray());
        Assert.Equal(new Half[4], secondFrame.Pixels.ToArray());
        Assert.Equal(1000f, firstFrame.SourcePeakNits);
        Assert.Equal(400f, secondFrame.SourcePeakNits);
    }

    [Fact]
    public void CopiesOnlyTheSuppliedSpanSlice()
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[]
        {
            Half.NaN,
            (Half)0.25f, (Half)0.5f, (Half)2, (Half)0.5f,
            Half.PositiveInfinity
        };

        var frame = new HdrFrame(info, pixels.AsSpan(1, 4), 1000);
        pixels[1] = (Half)0;

        Assert.Equal(new[] { (Half)0.25f, (Half)0.5f, (Half)2, (Half)0.5f },
            frame.Pixels.ToArray());
    }

    [Fact]
    public void RejectsMissingSurfaceInfo()
    {
        Assert.Throws<ArgumentNullException>(() => new HdrFrame(null!, new Half[4], 1000));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(23)]
    [InlineData(25)]
    public void RejectsPixelCountsThatDoNotMatchTheWholeSurface(int channelCount)
    {
        var info = new RenderSurfaceInfo(3, 2, 203);

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, new Half[channelCount], 1000));
    }

    [Theory]
    [InlineData(0, float.NaN)]
    [InlineData(1, float.NaN)]
    [InlineData(2, float.NaN)]
    [InlineData(3, float.NaN)]
    [InlineData(0, float.PositiveInfinity)]
    [InlineData(1, float.PositiveInfinity)]
    [InlineData(2, float.PositiveInfinity)]
    [InlineData(3, float.PositiveInfinity)]
    [InlineData(0, float.NegativeInfinity)]
    [InlineData(1, float.NegativeInfinity)]
    [InlineData(2, float.NegativeInfinity)]
    [InlineData(3, float.NegativeInfinity)]
    public void RejectsNonFiniteValuesInEveryChannel(int channel, float value)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)1, (Half)1, (Half)1, (Half)1 };
        pixels[channel] = (Half)value;

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, pixels, 1000));
    }

    [Theory]
    [InlineData(-0.5f)]
    [InlineData(1.5f)]
    public void RejectsAlphaOutsideTheUnitInterval(float alpha)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)0, (Half)0, (Half)0, (Half)alpha };

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, pixels, 1000));
    }

    [Theory]
    [InlineData(0, 1f)]
    [InlineData(1, 1f)]
    [InlineData(2, 1f)]
    [InlineData(0, -1f)]
    [InlineData(1, -1f)]
    [InlineData(2, -1f)]
    public void RejectsNonZeroColorUnderZeroAlpha(int channel, float value)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new Half[4];
        pixels[channel] = (Half)value;

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, pixels, 1000));
    }

    [Fact]
    public void RejectsEvenTheSmallestNonZeroHalfUnderZeroAlpha()
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { Half.Epsilon, (Half)0, (Half)0, (Half)0 };

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, pixels, 1000));
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(1f)]
    public void AcceptsBothAlphaEndpoints(float alpha)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)0, (Half)0, (Half)0, (Half)alpha };

        var frame = new HdrFrame(info, pixels, 1000);

        Assert.Equal(pixels, frame.Pixels.ToArray());
    }

    [Fact]
    public void PreservesNegativeHdrAndAboveAlphaColorWithoutClampingOrPremultiplyingAgain()
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { Half.MinValue, Half.MaxValue, (Half)2, (Half)0.5f };

        var frame = new HdrFrame(info, pixels, 30_000_000);

        Assert.Equal(pixels, frame.Pixels.ToArray());
    }

    [Fact]
    public void ChecksNonFiniteChannelsInTheLastPixel()
    {
        var info = new RenderSurfaceInfo(3, 2, 203);
        var pixels = new Half[info.ChannelCount];
        pixels[^2] = Half.NaN;
        pixels[^1] = (Half)1;

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, pixels, 1000));
    }

    [Fact]
    public void ChecksTransparentColorInTheLastPixel()
    {
        var info = new RenderSurfaceInfo(3, 2, 203);
        var pixels = new Half[info.ChannelCount];
        pixels[^2] = (Half)1;

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, pixels, 1000));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0f)]
    [InlineData(-1f)]
    public void RejectsNonPositiveOrNonFiniteSourcePeak(float sourcePeakNits)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);

        Assert.ThrowsAny<ArgumentException>(() => new HdrFrame(info, new Half[4], sourcePeakNits));
    }

    [Fact]
    public void PreservesDeclaredPeakAboveTheActualContentPeak()
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)2, (Half)1, (Half)0, (Half)1 };

        var frame = new HdrFrame(info, pixels, 1000);

        Assert.Equal(203f, frame.Info.ReferenceWhiteNits);
        Assert.Equal(1000f, frame.SourcePeakNits);
        Assert.Equal(pixels, frame.Pixels.ToArray());
    }

    [Fact]
    public void AcceptsPeakBelowReferenceWhiteWhenItCoversTheContent()
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)0.25f, (Half)0.125f, (Half)0, (Half)1 };

        var frame = new HdrFrame(info, pixels, 100);

        Assert.Equal(203f, frame.Info.ReferenceWhiteNits);
        Assert.Equal(100f, frame.SourcePeakNits);
        Assert.Equal(pixels, frame.Pixels.ToArray());
    }

    [Theory]
    [InlineData(0, 1f, 405f)]
    [InlineData(1, 1f, 405f)]
    [InlineData(2, 1f, 405f)]
    [InlineData(0, 0.5f, 810f)]
    [InlineData(1, 0.5f, 810f)]
    [InlineData(2, 0.5f, 810f)]
    public void RejectsPeakBelowAnyUnassociatedColorChannel(int channel, float alpha, float sourcePeakNits)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)0, (Half)0, (Half)0, (Half)alpha };
        pixels[channel] = (Half)2;

        var exception = Assert.Throws<ArgumentException>(() => new HdrFrame(info, pixels, sourcePeakNits));

        Assert.Equal("sourcePeakNits", exception.ParamName);
    }

    [Theory]
    [InlineData(1f, 406f)]
    [InlineData(0.5f, 812f)]
    public void AcceptsTheAdjacentHalfValueWithinPointOnePercentPeakTolerance(float alpha, float sourcePeakNits)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)2.001953125f, (Half)0, (Half)0, (Half)alpha };

        var frame = new HdrFrame(info, pixels, sourcePeakNits);

        Assert.Equal(sourcePeakNits, frame.SourcePeakNits);
        Assert.Equal(pixels, frame.Pixels.ToArray());
    }

    [Theory]
    [InlineData(1f, 406f)]
    [InlineData(0.5f, 812f)]
    public void RejectsTheNextHalfValueBeyondPointOnePercentPeakTolerance(float alpha, float sourcePeakNits)
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { (Half)2.00390625f, (Half)0, (Half)0, (Half)alpha };

        var exception = Assert.Throws<ArgumentException>(() => new HdrFrame(info, pixels, sourcePeakNits));

        Assert.Equal("sourcePeakNits", exception.ParamName);
    }

    [Fact]
    public void NegativeColorDoesNotIncreaseTheRequiredPositivePeak()
    {
        var info = new RenderSurfaceInfo(1, 1, 203);
        var pixels = new[] { Half.MinValue, (Half)(-2), (Half)0, Half.Epsilon };

        var frame = new HdrFrame(info, pixels, 1);

        Assert.Equal(1f, frame.SourcePeakNits);
        Assert.Equal(pixels, frame.Pixels.ToArray());
    }

    [Fact]
    public void ChecksThePeakOfTheLastPixel()
    {
        var info = new RenderSurfaceInfo(3, 2, 203);
        var pixels = new Half[info.ChannelCount];
        pixels[^2] = (Half)2;
        pixels[^1] = (Half)0.5f;

        var exception = Assert.Throws<ArgumentException>(() => new HdrFrame(info, pixels, 810));

        Assert.Equal("sourcePeakNits", exception.ParamName);
    }

    [Fact]
    public void PeakComparisonCannotOverflowFloatAndAcceptAnUnderstatedPeak()
    {
        var info = new RenderSurfaceInfo(1, 1, float.MaxValue);
        var pixels = new[] { (Half)2, (Half)0, (Half)0, (Half)1 };

        var exception = Assert.Throws<ArgumentException>(() => new HdrFrame(info, pixels, float.MaxValue));

        Assert.Equal("sourcePeakNits", exception.ParamName);
    }
}
