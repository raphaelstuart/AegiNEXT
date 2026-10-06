namespace AegiNext.Rendering.Tests;

public class LinearColorTests
{
    [Fact]
    public void DefaultIsTransparentBlack()
    {
        Assert.Equal(new LinearColor(0, 0, 0, 0), default);
    }

    [Fact]
    public void EncodedSrgbIsDecodedBeforeDrawing()
    {
        var color = LinearColor.FromSrgb(0.5f, 0.04045f, 1, 0.25f);
        Assert.Equal(0.214041f, color.Red, 5);
        Assert.Equal(0.0031308f, color.Green, 6);
        Assert.Equal(1, color.Blue);
        Assert.Equal(0.25f, color.Alpha);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(65505f)]
    [InlineData(-65505f)]
    public void RejectsUnrepresentableChannel(float channel)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearColor(channel, 0, 0, 1));
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(1.01f)]
    [InlineData(float.NaN)]
    public void RejectsInvalidAlpha(float alpha)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LinearColor(1, 1, 1, alpha));
    }

    [Fact]
    public void EncodedInputCannotSilentlyClip()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => LinearColor.FromSrgb(2, 0, 0, 1));
    }
}
