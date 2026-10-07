using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

public sealed class ProjectRenderSurfacePoolTests
{
    [Fact]
    public void RecursiveGroupsKeepIndependentSurfacesAndMatchFreshAllocationPixels()
    {
        var document = NestedDocument();
        using var renderer = Renderer();
        using var fresh = Renderer(0);
        using var actual = renderer.Render(document, MediaTime.Zero);
        using var expected = fresh.Render(document, MediaTime.Zero);
        Assert.Equal(Pixels(expected), Pixels(actual));
        Assert.Equal(3, renderer.RenderSurfaceStatistics.Allocations);
        Assert.Equal(3, renderer.RenderSurfaceStatistics.PeakActiveLeases);
        Assert.Equal(0, renderer.RenderSurfaceStatistics.ActiveLeases);
        Assert.Equal(3, renderer.RenderSurfaceStatistics.RetainedSurfaces);
        var allocations = renderer.RenderSurfaceStatistics.Allocations;
        for (var frame = 1; frame <= 3; frame++)
        {
            renderer.RenderInto(document, new(frame, 30), actual);
            fresh.RenderInto(document, new(frame, 30), expected);
            Assert.Equal(Pixels(expected), Pixels(actual));
        }

        Assert.Equal(allocations, renderer.RenderSurfaceStatistics.Allocations);
        Assert.True(renderer.RenderSurfaceStatistics.Reuses >= 13);
        Assert.Equal(3L * document.Width * document.Height * 8, renderer.RenderSurfaceStatistics.RetainedBytes);
    }

    [Fact]
    public void PreviewSizesAndReferenceWhitesReuseOnlyMatchingSurfaces()
    {
        var document = new ProjectDocument
        {
            Width = 32, Height = 24,
            Layers = [new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 12, 8),
                Transform = new(3, 2), Fill = new(4, 2, 0.5, 0.75), Blur = 1 }]
        };
        using var renderer = Renderer();
        using var fresh = Renderer(0);
        using var actual = renderer.Render(document, MediaTime.Zero);
        using var expected = fresh.Render(document, MediaTime.Zero);
        var original = Pixels(actual);
        foreach (var size in new[] { (Width: 16, Height: 12), (Width: 8, Height: 6) })
        {
            Assert.Equal(
                fresh.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, size.Width, size.Height),
                renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, size.Width, size.Height));
        }

        renderer.RenderInto(document, MediaTime.Zero, actual);
        Assert.Equal(original, Pixels(actual));
        Assert.Equal(Pixels(expected), Pixels(actual));
        Assert.Equal(3, renderer.RenderSurfaceStatistics.Allocations);
        var bright = document with { ReferenceWhiteNits = 406 };
        using var brightActual = renderer.Render(bright, MediaTime.Zero);
        using var brightExpected = fresh.Render(bright, MediaTime.Zero);
        Assert.Equal(Pixels(brightExpected), Pixels(brightActual));
        Assert.Equal(4, renderer.RenderSurfaceStatistics.Allocations);
        renderer.RenderInto(document, MediaTime.Zero, actual);
        Assert.Equal(original, Pixels(actual));
        Assert.Equal(4, renderer.RenderSurfaceStatistics.Allocations);
        Assert.Equal(0, renderer.RenderSurfaceStatistics.ActiveLeases);
    }

    [Fact]
    public void ResourceFailureReturnsEveryRecursiveLeaseAndTheNextFrameMatchesFreshRendering()
    {
        var resolver = new FailOnceProjectAssetResolver(new GeneratedImageProjectAssetResolver()) { FailNextOpen = true };
        var document = ImageGroupDocument();
        using var renderer = new ProjectSceneRenderer(resolver);
        using var fresh = new ProjectSceneRenderer(new GeneratedImageProjectAssetResolver(), null, 0);
        Assert.Throws<IOException>(() => renderer.Render(document, MediaTime.Zero));
        Assert.Equal(0, renderer.RenderSurfaceStatistics.ActiveLeases);
        Assert.Equal(2, renderer.RenderSurfaceStatistics.Allocations);
        using var actual = renderer.Render(document, MediaTime.Zero);
        using var expected = fresh.Render(document, MediaTime.Zero);
        Assert.Equal(Pixels(expected), Pixels(actual));
        Assert.Equal(2, renderer.RenderSurfaceStatistics.Allocations);
        Assert.True(renderer.RenderSurfaceStatistics.Reuses >= 4);
    }

    [Fact]
    public void ResourceCancellationReturnsEveryRecursiveLeaseAndPreviewRecovers()
    {
        using var cancellation = new CancellationTokenSource();
        var resolver = new CancellingProjectAssetResolver(new GeneratedImageProjectAssetResolver(), cancellation);
        var document = ImageGroupDocument();
        using var renderer = new ProjectSceneRenderer(resolver);
        using var fresh = new ProjectSceneRenderer(new GeneratedImageProjectAssetResolver(), null, 0);
        Assert.Throws<OperationCanceledException>(() =>
            renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 64, 32, cancellation.Token));
        Assert.Equal(0, renderer.RenderSurfaceStatistics.ActiveLeases);
        var allocations = renderer.RenderSurfaceStatistics.Allocations;
        Assert.Equal(
            fresh.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 64, 32),
            renderer.ComposePreview(document, MediaTime.Zero, BlackVideo(), 1, 1, 4, 64, 32));
        Assert.Equal(allocations, renderer.RenderSurfaceStatistics.Allocations);
        Assert.Equal(0, renderer.RenderSurfaceStatistics.ActiveLeases);
        Assert.True(renderer.RenderSurfaceStatistics.Reuses > 0);
    }

    [Fact]
    public void BoundedRendererReleasesOverflowWithoutChangingRecursivePixels()
    {
        var document = NestedDocument();
        var bytes = (long)document.Width * document.Height * 8;
        using var renderer = Renderer(bytes);
        using var fresh = Renderer(0);
        using var actual = renderer.Render(document, MediaTime.Zero);
        using var expected = fresh.Render(document, MediaTime.Zero);
        Assert.Equal(Pixels(expected), Pixels(actual));
        Assert.Equal(bytes, renderer.RenderSurfaceStatistics.RetainedBytes);
        Assert.Equal(2, renderer.RenderSurfaceStatistics.ReleasedSurfaces);
        renderer.RenderInto(document, new(1, 30), actual);
        fresh.RenderInto(document, new(1, 30), expected);
        Assert.Equal(Pixels(expected), Pixels(actual));
        Assert.Equal(bytes, renderer.RenderSurfaceStatistics.RetainedBytes);
        Assert.Equal(4, renderer.RenderSurfaceStatistics.ReleasedSurfaces);
        Assert.Equal(0, renderer.RenderSurfaceStatistics.ActiveLeases);
    }

    [Fact]
    public void RendererDisposalReleasesIdleSurfacesAndLeavesCallerOwnedResultsAvailable()
    {
        var document = NestedDocument();
        var renderer = Renderer();
        using var result = renderer.Render(document, MediaTime.Zero);
        var expected = Pixels(result);
        var allocations = renderer.RenderSurfaceStatistics.Allocations;
        renderer.Dispose();
        renderer.Dispose();
        Assert.Equal(0, renderer.RenderSurfaceStatistics.RetainedBytes);
        Assert.Equal(0, renderer.RenderSurfaceStatistics.RetainedSurfaces);
        Assert.Equal(allocations, renderer.RenderSurfaceStatistics.ReleasedSurfaces);
        Assert.Equal(expected, Pixels(result));
        Assert.Throws<ObjectDisposedException>(() => renderer.RenderInto(document, MediaTime.Zero, result));
    }

    private static ProjectSceneRenderer Renderer(long maximumRetainedBytes = RenderSurfacePool.DEFAULT_RETAINED_BYTES)
    {
        return new(new DirectoryProjectAssetResolver(AppContext.BaseDirectory), null, maximumRetainedBytes);
    }

    private static ProjectDocument NestedDocument()
    {
        return new()
        {
            Width = 32, Height = 24, ReferenceWhiteNits = 406,
            Layers = [new()
            {
                Kind = LayerKind.GROUP, Opacity = 0.75, Transform = new(3, 2), Children = [new()
                {
                    Kind = LayerKind.GROUP, Opacity = 0.5, Transform = new(4, 3), Children =
                    [
                        new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 8, 8), Fill = new(4, 2, 0.5, 0.75), Blur = 0.5 },
                        new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 5, 6), Transform = new(3, 2),
                            Fill = new(0, 1, 0, 0.5), Blend = BlendMode.SCREEN }
                    ]
                }]
            }]
        };
    }

    private static ProjectDocument ImageGroupDocument()
    {
        var image = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "generated-lease.png");
        return new()
        {
            Width = 128, Height = 64, Assets = [image], Layers = [new()
            {
                Kind = LayerKind.GROUP, Children =
                [
                    new() { Kind = LayerKind.SHAPE, Shape = new(ShapeKind.RECTANGLE, 128, 64), Fill = new(0.5, 0.25, 0, 0.5) },
                    new() { Kind = LayerKind.IMAGE, Image = new(image.Id, 24, 12), Transform = new(4, 2) }
                ]
            }]
        };
    }

    private static Half[] Pixels(LinearRenderSurface surface)
    {
        var result = new Half[surface.Info.ChannelCount];
        surface.CopyPixels(result);
        return result;
    }

    private static byte[] BlackVideo() => [0, 0, 0, 255];
}
