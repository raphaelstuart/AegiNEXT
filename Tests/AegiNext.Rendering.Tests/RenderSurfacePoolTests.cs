using System.Numerics;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

public sealed class RenderSurfacePoolTests
{
    [Fact]
    public void ReuseClearsPixelsAndRestoresMatrixClipAndSaveCount()
    {
        using var pool = new RenderSurfacePool();
        var info = new RenderSurfaceInfo(8, 6, 203);
        var lease = pool.Rent(info);
        var surface = lease.Surface;
        Assert.Equal(2, surface.Canvas.SaveCount);
        surface.FillRectangle(Vector2.Zero, new(8, 6), new(4, 2, 1, 1));
        surface.Canvas.Translate(5, 2);
        surface.Canvas.ClipRect(new(0, 0, 1, 1));
        surface.Canvas.Save();
        surface.Canvas.Scale(3, 2);
        surface.Canvas.Save();
        lease.Dispose();

        Assert.Throws<ObjectDisposedException>(() => lease.Surface);
        using var reused = pool.Rent(info);
        Assert.Same(surface, reused.Surface);
        Assert.Equal(SKMatrix.Identity, reused.Surface.Canvas.TotalMatrix);
        Assert.Equal(new SKRectI(0, 0, 8, 6), reused.Surface.Canvas.DeviceClipBounds);
        Assert.Equal(2, reused.Surface.Canvas.SaveCount);
        var pixels = new Half[info.ChannelCount];
        reused.Surface.CopyPixels(pixels);
        Assert.All(pixels, value => Assert.Equal((Half)0, value));
        reused.Surface.FillRectangle(Vector2.Zero, new(8, 6), new(1, 1, 1, 1));
        reused.Surface.CopyPixels(pixels);
        Assert.All(pixels, value => Assert.Equal((Half)1, value));
        Assert.Equal(1, pool.Statistics.Allocations);
        Assert.Equal(1, pool.Statistics.Reuses);
        Assert.Equal(1, pool.Statistics.ActiveLeases);
        Assert.Equal(0, pool.Statistics.RetainedBytes);
    }

    [Fact]
    public void WidthHeightAndReferenceWhiteMustAllMatch()
    {
        using var pool = new RenderSurfacePool();
        LinearRenderSurface? original = null;
        foreach (var info in new RenderSurfaceInfo[] { new(2, 3, 203), new(3, 2, 203), new(2, 3, 406) })
        {
            using var lease = pool.Rent(info);
            original ??= lease.Surface;
        }

        using (var lease = pool.Rent(new(2, 3, 203)))
        {
            Assert.Same(original, lease.Surface);
        }

        Assert.Equal(3, pool.Statistics.Allocations);
        Assert.Equal(1, pool.Statistics.Reuses);
        Assert.Equal(3, pool.Statistics.RetainedSurfaces);
        Assert.Equal(3 * 2 * 3 * 8, pool.Statistics.RetainedBytes);
    }

    [Fact]
    public void ActiveLeasesAreIndependentAndDoNotConsumeTheIdleBudget()
    {
        var info = new RenderSurfaceInfo(4, 4, 203);
        using var pool = new RenderSurfacePool(info.ByteCount);
        var first = pool.Rent(info);
        var second = pool.Rent(info);
        var third = pool.Rent(info);
        Assert.NotSame(first.Surface, second.Surface);
        Assert.NotSame(first.Surface, third.Surface);
        Assert.NotSame(second.Surface, third.Surface);
        first.Surface.FillRectangle(Vector2.Zero, new(4, 4), new(4, 0, 0, 1));
        var pixels = new Half[info.ChannelCount];
        second.Surface.CopyPixels(pixels);
        Assert.All(pixels, value => Assert.Equal((Half)0, value));
        Assert.Equal(3, pool.Statistics.ActiveLeases);
        Assert.Equal(3, pool.Statistics.PeakActiveLeases);
        Assert.Equal(0, pool.Statistics.RetainedBytes);
        first.Dispose();
        second.Dispose();
        third.Dispose();
        Assert.Equal(info.ByteCount, pool.Statistics.RetainedBytes);
        Assert.Equal(1, pool.Statistics.RetainedSurfaces);
        Assert.Equal(2, pool.Statistics.ReleasedSurfaces);
        Assert.Equal(0, pool.Statistics.ActiveLeases);
    }

    [Fact]
    public void OversizedAndZeroBudgetSurfacesAreReleasedRatherThanRetained()
    {
        using var pool = new RenderSurfacePool(128);
        var retained = pool.Rent(new(4, 4, 203));
        var retainedSurface = retained.Surface;
        retained.Dispose();
        var oversized = pool.Rent(new(5, 4, 203));
        var oversizedSurface = oversized.Surface;
        oversized.Dispose();
        Assert.Throws<ObjectDisposedException>(() => oversizedSurface.CopyPixels(new Half[80]));
        Assert.Equal(128, pool.Statistics.RetainedBytes);
        Assert.Equal(1, pool.Statistics.ReleasedSurfaces);
        pool.Dispose();
        pool.Dispose();
        Assert.Equal(0, pool.Statistics.RetainedBytes);
        Assert.Equal(2, pool.Statistics.ReleasedSurfaces);
        Assert.Throws<ObjectDisposedException>(() => retainedSurface.CopyPixels(new Half[64]));

        using var disabled = new RenderSurfacePool(0);
        using (var lease = disabled.Rent(new(4, 4, 203)))
        {
            Assert.Equal(1, disabled.Statistics.ActiveLeases);
        }

        Assert.Equal(0, disabled.Statistics.RetainedBytes);
        Assert.Equal(1, disabled.Statistics.ReleasedSurfaces);
        Assert.Throws<ArgumentOutOfRangeException>(() => new RenderSurfacePool(-1));
    }

    [Fact]
    public void ExceptionalLeaseIsReturnedAndCanBeReused()
    {
        using var pool = new RenderSurfacePool();
        var info = new RenderSurfaceInfo(4, 4, 203);
        Assert.Throws<IOException>((Action)(() =>
        {
            using var lease = pool.Rent(info);
            lease.Surface.FillRectangle(Vector2.Zero, new(4, 4), new(1, 0, 0, 1));
            lease.Surface.Canvas.ClipRect(new(0, 0, 1, 1));
            lease.Surface.Canvas.Save();
            throw new IOException("Controlled draw failure.");
        }));
        Assert.Equal(0, pool.Statistics.ActiveLeases);
        using var recovered = pool.Rent(info);
        var pixels = new Half[info.ChannelCount];
        recovered.Surface.CopyPixels(pixels);
        Assert.All(pixels, value => Assert.Equal((Half)0, value));
        Assert.Equal(1, pool.Statistics.Allocations);
        Assert.Equal(1, pool.Statistics.Reuses);
    }

    [Fact]
    public void PoolDisposalRejectsNewRentalsAndLateLeaseDisposalReleasesItsSurfaceOnce()
    {
        var pool = new RenderSurfacePool();
        var lease = pool.Rent(new(4, 4, 203));
        var surface = lease.Surface;
        pool.Dispose();
        Assert.Throws<ObjectDisposedException>(() => pool.Rent(new(4, 4, 203)));
        surface.FillRectangle(Vector2.Zero, new(4, 4), new(1, 0, 0, 1));
        Assert.Equal(1, pool.Statistics.ActiveLeases);
        lease.Dispose();
        lease.Dispose();
        Assert.Equal(0, pool.Statistics.ActiveLeases);
        Assert.Equal(0, pool.Statistics.RetainedBytes);
        Assert.Equal(1, pool.Statistics.ReleasedSurfaces);
        Assert.Throws<ObjectDisposedException>(() => surface.CopyPixels(new Half[64]));
    }
}
