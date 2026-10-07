using System.Numerics;

namespace AegiNext.Rendering.Tests;

public class LinearRenderSurfaceTests
{
    [Fact]
    public void LinearColorIsNotDecodedAgain()
    {
        using var surface = new LinearRenderSurface(new(3, 2, 203));
        surface.FillRectangle(Vector2.Zero, new(3, 2), new(0.5f, 0.5f, 0.5f, 1));
        AssertPixel(surface, 0, 0, 0.5f, 0.5f, 0.5f, 1);
    }

    [Fact]
    public void ExtendedRangeSurvivesPremultiplicationAndCopy()
    {
        using var surface = new LinearRenderSurface(new(3, 2, 203));
        surface.FillRectangle(Vector2.Zero, new(3, 2), new(4, -0.5f, 2, 0.5f));
        AssertPixel(surface, 2, 1, 2, -0.25f, 1, 0.5f);
        Assert.Equal(203, surface.Info.ReferenceWhiteNits);
    }

    [Theory]
    [InlineData(65504f)]
    [InlineData(-65504f)]
    public void HalfRangeBoundaryRemainsFiniteThroughCompositing(float value)
    {
        using var target = new LinearRenderSurface(new(1, 1, 203));
        using var source = new LinearRenderSurface(new(1, 1, 203));
        source.FillRectangle(Vector2.Zero, Vector2.One, new(value, 0, 0, 1));
        target.Composite(source, Vector2.Zero, 1);
        var pixels = new Half[4];
        target.CopyPixels(pixels);
        Assert.True(Half.IsFinite(pixels[0]));
        Assert.Equal((Half)value, pixels[0]);
    }

    [Fact]
    public void ZeroOpacityLeavesDestinationUnchanged()
    {
        using var target = new LinearRenderSurface(new(1, 1, 203));
        using var source = new LinearRenderSurface(new(1, 1, 203));
        target.FillRectangle(Vector2.Zero, Vector2.One, new(0, 2, 0, 1));
        source.FillRectangle(Vector2.Zero, Vector2.One, new(4, 0, 0, 1));
        target.Composite(source, Vector2.Zero, 0);
        AssertPixel(target, 0, 0, 0, 2, 0, 1);
    }

    [Fact]
    public void SourceOverBlendsInLinearSpaceAndPreservesHighlights()
    {
        using var background = new LinearRenderSurface(new(3, 2, 203));
        using var foreground = new LinearRenderSurface(new(3, 2, 203));
        background.FillRectangle(Vector2.Zero, new(3, 2), new(0, 0, 2, 1));
        foreground.FillRectangle(Vector2.Zero, new(3, 2), new(4, 0, 0, 0.5f));
        background.Composite(foreground, Vector2.Zero, 1);
        AssertPixel(background, 1, 1, 2, 0, 1, 1);
    }

    [Fact]
    public void LayerOpacityIsAppliedAfterInternalOverlap()
    {
        using var target = new LinearRenderSurface(new(3, 2, 203));
        using var layer = new LinearRenderSurface(new(3, 2, 203));
        layer.FillRectangle(Vector2.Zero, new(3, 2), new(1, 0, 0, 1));
        layer.FillRectangle(Vector2.Zero, new(3, 2), new(0, 1, 0, 1));
        target.Composite(layer, Vector2.Zero, 0.5f);
        AssertPixel(target, 1, 1, 0, 0.5f, 0, 0.5f);
    }

    [Fact]
    public void FractionalLayerOpacityIsNotQuantizedToEightBits()
    {
        using var target = new LinearRenderSurface(new(1, 1, 203));
        using var source = new LinearRenderSurface(new(1, 1, 203));
        source.FillRectangle(Vector2.Zero, Vector2.One, new(4, 0, 0, 1));
        target.Composite(source, Vector2.Zero, 0.123f);
        var pixels = new Half[4];
        target.CopyPixels(pixels);
        Assert.InRange(Math.Abs((float)pixels[0] - 0.492f), 0, 0.0005f);
        Assert.InRange(Math.Abs((float)pixels[3] - 0.123f), 0, 0.0001f);
    }

    [Fact]
    public void TransparentHdrPaintCannotLeaveColorInAnEmptySurface()
    {
        using var surface = new LinearRenderSurface(new(1, 1, 203));
        surface.FillRectangle(Vector2.Zero, Vector2.One, new(4, -2, 6, 0));
        AssertPixel(surface, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void CompositionPreservesSignedRangeAfterSourceDisposal()
    {
        using var target = new LinearRenderSurface(new(3, 2, 203));
        var source = new LinearRenderSurface(new(1, 1, 203));
        source.FillRectangle(Vector2.Zero, Vector2.One, new(-0.5f, 2, 4, 1));
        target.Composite(source, new(2, 1), 1);
        source.Dispose();
        AssertPixel(target, 2, 1, -0.5f, 2, 4, 1);
        AssertPixel(target, 0, 0, 0, 0, 0, 0);
    }

    [Fact]
    public void CopyUsesTopDownTightlyPackedRgbaWithoutBorrowingMemory()
    {
        using var surface = new LinearRenderSurface(new(3, 2, 203));
        surface.FillRectangle(Vector2.Zero, Vector2.One, new(1, 2, 3, 1));
        surface.FillRectangle(new(2, 1), Vector2.One, new(4, 5, 6, 1));
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        surface.Clear();
        Assert.Equal((Half)1, pixels[0]);
        Assert.Equal((Half)4, pixels[20]);
        Assert.Equal((Half)5, pixels[21]);
        Assert.Equal((Half)6, pixels[22]);
        Assert.Equal((Half)1, pixels[23]);
        AssertPixel(surface, 2, 1, 0, 0, 0, 0);
    }

    [Fact]
    public void FloatCopyMatchesHalfSamplesAndLeavesExtraBufferUntouched()
    {
        using var surface = new LinearRenderSurface(new(3, 2, 203));
        surface.FillRectangle(Vector2.Zero, Vector2.One, new(4, -0.5f, 2, 0.5f));
        surface.FillRectangle(new(2, 1), Vector2.One, new(65504, -65504, 0.123f, 1));
        var half = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(half);
        var pixels = new float[surface.Info.ChannelCount + 4];
        pixels.AsSpan(surface.Info.ChannelCount).Fill(42);

        surface.CopyPixels(pixels);
        surface.Clear();

        for (var index = 0; index < half.Length; index++)
        {
            Assert.Equal((float)half[index], pixels[index]);
        }

        Assert.All(pixels.Skip(surface.Info.ChannelCount), value => Assert.Equal(42, value));
        Assert.Equal(2, pixels[0]);
        Assert.Equal(-0.25f, pixels[1]);
        Assert.Equal(0.5f, pixels[3]);
        Assert.Equal(65504, pixels[20]);
        Assert.Equal(-65504, pixels[21]);
    }

    [Fact]
    public void FloatCopyPreservesAntialiasedPremultipliedSamples()
    {
        using var surface = new LinearRenderSurface(new(16, 16, 203));
        surface.FillEllipse(new(1.25f, 1.25f), new(13.5f, 13.5f), new(4, -2, 0, 0.5f));
        var half = new Half[surface.Info.ChannelCount];
        var pixels = new float[surface.Info.ChannelCount];
        surface.CopyPixels(half);

        surface.CopyPixels(pixels);

        for (var index = 0; index < pixels.Length; index++)
        {
            Assert.Equal((float)half[index], pixels[index]);
        }

        Assert.Contains(Enumerable.Range(0, pixels.Length / 4), index => pixels[index * 4 + 3] is > 0 and < 0.49f);
    }

    [Fact]
    public void FloatCopyRejectsShortBufferAndDisposedSurface()
    {
        using var surface = new LinearRenderSurface(new(2, 2, 203));
        Assert.Throws<ArgumentException>(() => surface.CopyPixels(new float[15]));
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(() => surface.CopyPixels(new float[16]));
    }

    [Fact]
    public void AntialiasedEdgesRemainPremultiplied()
    {
        using var layer = new LinearRenderSurface(new(16, 16, 203));
        layer.FillEllipse(new(1.25f, 1.25f), new(13.5f, 13.5f), new(4, 2, 0, 0.5f));
        var pixels = new Half[layer.Info.ChannelCount];
        layer.CopyPixels(pixels);
        var edgeCount = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = (float)pixels[i + 3];
            if (alpha is > 0 and < 0.49f)
            {
                edgeCount++;
                Assert.InRange(Math.Abs((float)pixels[i] - 4 * alpha), 0, 0.004f);
                Assert.InRange(Math.Abs((float)pixels[i + 1] - 2 * alpha), 0, 0.004f);
            }
        }

        Assert.True(edgeCount > 0);

        using var composite = new LinearRenderSurface(layer.Info);
        composite.FillRectangle(Vector2.Zero, new(16, 16), new(0, 0, 2, 1));
        composite.Composite(layer, Vector2.Zero, 1);
        var result = new Half[pixels.Length];
        composite.CopyPixels(result);
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = (float)pixels[i + 3];
            Assert.InRange(Math.Abs((float)result[i] - (float)pixels[i]), 0, 0.004f);
            Assert.InRange(Math.Abs((float)result[i + 1] - (float)pixels[i + 1]), 0, 0.004f);
            Assert.InRange(Math.Abs((float)result[i + 2] - 2 * (1 - alpha)), 0, 0.004f);
            Assert.Equal((Half)1, result[i + 3]);
        }
    }

    [Fact]
    public void CompositeRejectsDifferentLuminanceScales()
    {
        using var target = new LinearRenderSurface(new(1, 1, 203));
        using var source = new LinearRenderSurface(new(1, 1, 80));
        Assert.Throws<ArgumentException>(() => target.Composite(source, Vector2.Zero, 1));
    }

    [Fact]
    public void DisposedSurfaceRejectsDrawingAndReading()
    {
        var surface = new LinearRenderSurface(new(1, 1, 203));
        surface.Dispose();
        surface.Dispose();
        Assert.Throws<ObjectDisposedException>(surface.Clear);
        Assert.Throws<ObjectDisposedException>(() => surface.CopyPixels(new Half[4]));
        using var target = new LinearRenderSurface(new(1, 1, 203));
        Assert.Throws<ObjectDisposedException>(() => target.Composite(surface, Vector2.Zero, 1));
    }

    [Fact]
    public void BufferAndCoordinatesAreValidatedBeforeNativeCalls()
    {
        using var surface = new LinearRenderSurface(new(2, 2, 203));
        Assert.Throws<ArgumentException>(() => surface.CopyPixels(new Half[15]));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.FillRectangle(new(float.NaN, 0), Vector2.One, default));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.FillEllipse(Vector2.Zero, new(-1, 1), default));
        Assert.Throws<ArgumentOutOfRangeException>(() => surface.Composite(surface, Vector2.Zero, 2));
    }

    [Theory]
    [InlineData(0, 1, 203)]
    [InlineData(1, -1, 203)]
    [InlineData(1, 1, 0)]
    [InlineData(1, 1, float.NaN)]
    public void RejectsInvalidSurfaceDescription(int width, int height, float white)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderSurfaceInfo(width, height, white));
    }

    [Fact]
    public void RejectsPixelStorageOverflow()
    {
        Assert.Throws<OverflowException>(() => new RenderSurfaceInfo(int.MaxValue, 2, 203));
    }

    private static void AssertPixel(LinearRenderSurface surface, int x, int y, params float[] expected)
    {
        var pixels = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(pixels);
        for (var c = 0; c < 4; c++)
        {
            Assert.InRange(Math.Abs((float)pixels[(y * surface.Info.Width + x) * 4 + c] - expected[c]), 0, 0.004f);
        }
    }
}
