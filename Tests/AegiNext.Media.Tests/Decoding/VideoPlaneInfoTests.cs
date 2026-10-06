using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

public sealed class VideoPlaneInfoTests
{
    [Fact]
    public void PreservesNegativeStrideWithoutIncludingPaddingInByteCount()
    {
        var info = new VideoPlaneInfo(1, 128, 48, -160);

        Assert.Equal(1, info.Index);
        Assert.Equal(-160, info.SourceStride);
        Assert.Equal(128, info.RowBytes);
        Assert.Equal(48, info.Height);
        Assert.Equal(6144, info.ByteCount);
    }

    [Fact]
    public void MinimumSignedStrideDoesNotOverflowAbsoluteValueValidation()
    {
        var info = new VideoPlaneInfo(0, 1, 1, int.MinValue);

        Assert.Equal(int.MinValue, info.SourceStride);
        Assert.Equal(1, info.ByteCount);
    }

    [Fact]
    public void SingleRowPaletteDoesNotRequireASecondRowStride()
    {
        var info = new VideoPlaneInfo(1, 1024, 1, 0);

        Assert.Equal(0, info.SourceStride);
        Assert.Equal(1024, info.ByteCount);
    }

    [Theory]
    [InlineData(-1, 4, 2, 4)]
    [InlineData(0, 0, 2, 4)]
    [InlineData(0, 4, 0, 4)]
    [InlineData(0, 4, 2, 3)]
    [InlineData(0, 4, 2, -3)]
    public void RejectsInvalidPlaneDimensionsAndUndersizedStrides(int index, int rowBytes, int height, int stride)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VideoPlaneInfo(index, rowBytes, height, stride));
    }

    [Fact]
    public void TightByteCountCannotWrapIntoASmallerManagedAllocation()
    {
        var info = new VideoPlaneInfo(0, 1 << 30, 2, int.MinValue);

        Assert.Throws<OverflowException>(() => info.ByteCount);
    }
}
