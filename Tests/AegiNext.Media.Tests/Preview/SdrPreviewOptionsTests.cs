using AegiNext.Media.Preview;

namespace AegiNext.Media.Tests.Preview;

public sealed class SdrPreviewOptionsTests
{
    [Fact]
    public void DefaultBoundsAre1280By720()
    {
        var options = new SdrPreviewOptions();

        Assert.Equal(1280, options.MaximumWidth);
        Assert.Equal(720, options.MaximumHeight);
    }

    [Fact]
    public void ExplicitNonSquareBoundsRemainIndependent()
    {
        var options = new SdrPreviewOptions(641, 359);

        Assert.Equal(641, options.MaximumWidth);
        Assert.Equal(359, options.MaximumHeight);
    }

    [Theory]
    [InlineData(0, 720)]
    [InlineData(-1, 720)]
    [InlineData(1280, 0)]
    [InlineData(1280, -1)]
    [InlineData(8193, 1)]
    [InlineData(1, 8193)]
    [InlineData(8192, 2049)]
    [InlineData(4097, 4096)]
    public void RejectsInvalidOrOverBudgetBounds(int width, int height)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SdrPreviewOptions(width, height));
    }

    [Theory]
    [InlineData(8192, 2048)]
    [InlineData(2048, 8192)]
    [InlineData(4096, 4096)]
    public void AcceptsTheInclusivePixelBudgetBoundary(int width, int height)
    {
        var options = new SdrPreviewOptions(width, height);

        Assert.Equal(width, options.MaximumWidth);
        Assert.Equal(height, options.MaximumHeight);
    }
}
