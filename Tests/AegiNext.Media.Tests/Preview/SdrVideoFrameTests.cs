using AegiNext.Media.Preview;

namespace AegiNext.Media.Tests.Preview;

public sealed class SdrVideoFrameTests
{
    [Fact]
    public void CopiesOpaqueBgraPixelsWithoutChangingRowOrChannelOrder()
    {
        var pixels = new byte[]
        {
            1, 2, 3, 255, 4, 5, 6, 255, 7, 8, 9, 255,
            10, 11, 12, 255, 13, 14, 15, 255, 16, 17, 18, 255
        };
        var expected = pixels.ToArray();

        var frame = new SdrVideoFrame(3, 2, pixels);
        Array.Fill(pixels, (byte)0);

        Assert.Equal(3, frame.Width);
        Assert.Equal(2, frame.Height);
        Assert.Equal(expected, frame.Pixels.ToArray());
    }

    [Fact]
    public void RejectsNullPixels()
    {
        Assert.Throws<ArgumentNullException>(() => new SdrVideoFrame(1, 1, null!));
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    [InlineData(4097, 4096)]
    [InlineData(int.MaxValue, int.MaxValue)]
    public void RejectsInvalidGeometryBeforeAllocatingPixels(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SdrVideoFrame(width, height, []));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(8)]
    public void RequiresTheExactTightPixelCount(int length)
    {
        Assert.Throws<ArgumentException>(() => new SdrVideoFrame(1, 1, new byte[length]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(254)]
    public void RejectsNonOpaqueAlpha(int alpha)
    {
        Assert.Throws<ArgumentException>(() => new SdrVideoFrame(1, 1, [10, 20, 30, (byte)alpha]));
    }
}
