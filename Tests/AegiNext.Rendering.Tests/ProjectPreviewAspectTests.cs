using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ProjectPreviewAspectTests
{
    [Fact]
    public void PortraitProjectKeepsVideoAspectAndSceneCoordinatesInTheSameViewport()
    {
        var document = new ProjectDocument
        {
            Width = 4,
            Height = 8,
            ReferenceWhiteNits = 203,
            Layers = [new ProjectLayer
            {
                Kind = LayerKind.SHAPE,
                Shape = new(ShapeKind.RECTANGLE, 2, 2),
                Fill = new(1, 0, 0)
            }]
        };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var background = GreenVideo(8, 4);
        var pixels = renderer.ComposePreview(document, MediaTime.Zero, background, 8, 4, 32, 4, 8);

        Assert.Equal(4 * 8 * 4, pixels.Length);
        AssertPixel(pixels, 4, 0, 0, 0, 0, 255);
        AssertPixel(pixels, 4, 3, 0, 0, 0, 0);
        AssertPixel(pixels, 4, 0, 3, 0, 255, 0);
        AssertPixel(pixels, 4, 3, 4, 0, 255, 0);
        AssertPixel(pixels, 4, 3, 7, 0, 0, 0);

        var resized = renderer.ComposePreview(document, MediaTime.Zero, background, 8, 4, 32, 8, 16);
        Assert.Equal(8 * 16 * 4, resized.Length);
        AssertPixel(resized, 8, 2, 2, 0, 0, 255);
        AssertPixel(resized, 8, 7, 6, 0, 255, 0);
        AssertPixel(resized, 8, 7, 15, 0, 0, 0);
    }

    [Fact]
    public void EmptySceneStillProducesAnOpaqueLetterboxedProjectPreview()
    {
        var document = new ProjectDocument { Width = 4, Height = 8 };
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var pixels = renderer.ComposePreview(document, MediaTime.Zero, GreenVideo(8, 4), 8, 4, 32, 4, 8);
        AssertPixel(pixels, 4, 0, 0, 0, 0, 0);
        AssertPixel(pixels, 4, 0, 3, 0, 255, 0);
        AssertPixel(pixels, 4, 0, 7, 0, 0, 0);
    }

    [Fact]
    public void InactiveLayersBypassCompositionWithoutCopyingRowPaddingOrLosingPixels()
    {
        var document = new ProjectDocument
        {
            Width = 2, Height = 2,
            Layers = [new() { Kind = LayerKind.SHAPE, Start = new(1), End = new(2), Shape = new(ShapeKind.RECTANGLE, 2, 2) }]
        };
        byte[] background = [7, 19, 83, 255, 21, 75, 127, 255, 99, 99, 99, 99,
            135, 163, 201, 255, 215, 239, 253, 255, 99, 99, 99, 99];
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        Assert.False(renderer.HasPreviewLayers(document, MediaTime.Zero));
        Assert.True(renderer.HasPreviewLayers(document, new(1)));
        Assert.False(renderer.HasPreviewLayers(document, new(2)));
        var pixels = renderer.ComposePreview(document, MediaTime.Zero, background, 2, 2, 12, 2, 2);
        Assert.Equal(background[..8].Concat(background[12..20]), pixels);
        var visibleDocument = document with
        {
            Layers = [document.Layers[0] with { Start = MediaTime.Zero, Fill = new(1, 0, 0) }]
        };
        Assert.True(renderer.HasPreviewLayers(visibleDocument, MediaTime.Zero));
        AssertPixel(renderer.ComposePreview(visibleDocument, MediaTime.Zero, background, 2, 2, 12, 2, 2),
            2, 0, 0, 0, 0, 255);
        Assert.False(renderer.HasPreviewLayers(document, MediaTime.Zero));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            renderer.ComposePreview(document, MediaTime.Zero, background, 2, 2, 12, 2, 2, cancellation.Token));
    }

    [Fact]
    public void NonOpaqueBackgroundRetainsTheLinearCompositionPath()
    {
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var pixels = renderer.ComposePreview(new() { Width = 1, Height = 1 }, MediaTime.Zero,
            [0, 255, 0, 0], 1, 1, 4, 1, 1);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, pixels);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(32769, 1)]
    [InlineData(32768, 1024)]
    public void PreviewRejectsInvalidOrOversizedTargetsBeforeAllocation(int width, int height)
    {
        using var renderer = new ProjectSceneRenderer(new DirectoryProjectAssetResolver(AppContext.BaseDirectory));
        var background = GreenVideo(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            renderer.ComposePreview(new(), MediaTime.Zero, background, 1, 1, 4, width, height));
    }

    private static byte[] GreenVideo(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index + 1] = 255;
            pixels[index + 3] = 255;
        }
        return pixels;
    }

    private static void AssertPixel(byte[] pixels, int width, int x, int y, byte blue, byte green, byte red)
    {
        var offset = (y * width + x) * 4;
        Assert.Equal(blue, pixels[offset]);
        Assert.Equal(green, pixels[offset + 1]);
        Assert.Equal(red, pixels[offset + 2]);
        Assert.Equal((byte)255, pixels[offset + 3]);
    }
}
