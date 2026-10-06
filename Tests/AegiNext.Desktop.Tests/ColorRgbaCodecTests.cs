using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class ColorRgbaCodecTests
{
    [Theory]
    [InlineData("255,0,0,255", "#FF0000FF")]
    [InlineData(" 32, 64, 128, 128 ", "#20408080")]
    [InlineData("0,0,0,0", "#00000000")]
    public void ByteRgbaMatchesTheHexColorAndOrder(string text, string hex)
    {
        Assert.True(ColorRgbaCodec.TryParse(text, 0.73, true, out var rgba));
        Assert.True(ColorHexCodec.TryParse(hex, 0.73, true, out var expected));
        Assert.Equal(expected, rgba);
        Assert.Equal(hex, ColorHexCodec.Format(rgba, true));
    }

    [Theory]
    [InlineData("256,0,0,255")]
    [InlineData("-1,0,0,255")]
    [InlineData("1.5,0,0,255")]
    [InlineData("1e2,0,0,255")]
    [InlineData("1,2,3")]
    [InlineData("1,2,3,")]
    [InlineData("1,2,3,4,5")]
    [InlineData("1;2;3;4")]
    public void InvalidOrUnfinishedRgbaIsRejected(string text)
    {
        Assert.False(ColorRgbaCodec.TryParse(text, 1, true, out _));
    }

    [Fact]
    public void DisabledAlphaKeepsItsOriginalValueAndDisplayDoesNotChangeHdr()
    {
        Assert.True(ColorRgbaCodec.TryParse("32,64,128,0", 0.731234567, false, out var rgba));
        Assert.Equal(0.731234567, rgba.Alpha);
        var hdr = new SceneColor(2.5, -0.1, 0.5, 0.5);
        Assert.Equal("255,0,188,128", ColorRgbaCodec.Format(hdr));
        Assert.Equal(2.5, hdr.Red);
    }
}
