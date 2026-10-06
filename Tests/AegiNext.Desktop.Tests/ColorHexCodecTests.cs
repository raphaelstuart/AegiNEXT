using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class ColorHexCodecTests
{
    [Theory]
    [InlineData("#FF000080", 128d / 255)]
    [InlineData("  #ff0000  ", 1)]
    public void HexUsesSrgbAndSuffixAlpha(string text, double alpha)
    {
        Assert.True(ColorHexCodec.TryParse(text, 0.73, true, out var value));
        Assert.Equal(new SceneColor(1, 0, 0, alpha), value);
    }

    [Fact]
    public void MidtoneIsDecodedToLinearAndDisabledAlphaRemainsUnchanged()
    {
        Assert.True(ColorHexCodec.TryParse("#80808040", 0.73, false, out var value));
        var expected = Math.Pow((128d / 255 + 0.055) / 1.055, 2.4);
        Assert.InRange(Math.Abs(expected - value.Red), 0, 0.0000001);
        Assert.Equal(value.Red, value.Green);
        Assert.Equal(value.Red, value.Blue);
        Assert.Equal(0.73, value.Alpha);
    }

    [Theory]
    [InlineData("FF0000")]
    [InlineData("#F00")]
    [InlineData("#1234567")]
    [InlineData("#123456789")]
    [InlineData("#GG000080")]
    public void HexRejectsAmbiguousOrUnfinishedInputs(string text)
    {
        Assert.False(ColorHexCodec.TryParse(text, 1, true, out _));
    }

    [Fact]
    public void DisplayMappingDoesNotMutateHdrSource()
    {
        var hdr = new SceneColor(2.5, -0.1, 0.5, 0.5);
        Assert.Equal("#FF00BC80", ColorHexCodec.Format(hdr, true));
        Assert.Equal("#FF00BC", ColorHexCodec.Format(hdr, false));
        Assert.Equal(2.5, hdr.Red);
        Assert.Equal(-0.1, hdr.Green);
    }
}
