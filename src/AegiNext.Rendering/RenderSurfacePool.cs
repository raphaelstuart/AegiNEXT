using SkiaSharp;

namespace AegiNext.Rendering;

internal sealed class RenderSurfacePool : IDisposable
{
    internal const long DEFAULT_RETAINED_BYTES = 64L * 1024 * 1024;
    private readonly List<LinearRenderSurface> retained = [];
    private readonly long maximumRetainedBytes;
    private readonly GRContext? graphicsContext;
    private long allocations;
    private long reuses;
    private long releasedSurfaces;
    private long retainedBytes;
    private int activeLeases;
    private int peakActiveLeases;
    private bool isDisposed;

    internal RenderSurfacePool(long maximumRetainedBytes = DEFAULT_RETAINED_BYTES, GRContext? graphicsContext = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetainedBytes);
        this.maximumRetainedBytes = maximumRetainedBytes;
        this.graphicsContext = graphicsContext;
    }

    internal RenderSurfacePoolStatistics Statistics => new(allocations, reuses, releasedSurfaces,
        retainedBytes, retained.Count, activeLeases, peakActiveLeases, maximumRetainedBytes);

    internal RenderSurfaceLease Rent(RenderSurfaceInfo info)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(info);
        LinearRenderSurface? surface = null;
        for (var index = retained.Count - 1; index >= 0; index--)
        {
            if (retained[index].Info == info)
            {
                surface = retained[index];
                retained.RemoveAt(index);
                retainedBytes -= info.ByteCount;
                break;
            }
        }

        var reused = surface is not null;
        if (surface is null)
        {
            surface = new(info, graphicsContext);
            allocations++;
        }

        try
        {
            surface.Canvas.Save();
            var lease = new RenderSurfaceLease(this, surface);
            activeLeases++;
            peakActiveLeases = Math.Max(peakActiveLeases, activeLeases);
            if (reused)
            {
                reuses++;
            }

            return lease;
        }
        catch
        {
            Release(surface);
            throw;
        }
    }

    internal void Return(LinearRenderSurface surface)
    {
        activeLeases--;
        if (isDisposed || surface.Info.ByteCount > maximumRetainedBytes - retainedBytes)
        {
            Release(surface);
            return;
        }

        try
        {
            var canvas = surface.Canvas;
            canvas.RestoreToCount(1);
            canvas.ResetMatrix();
            surface.Clear();
            retained.Add(surface);
            retainedBytes += surface.Info.ByteCount;
        }
        catch
        {
            Release(surface);
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        foreach (var surface in retained)
        {
            Release(surface);
        }

        retained.Clear();
        retainedBytes = 0;
    }

    private void Release(LinearRenderSurface surface)
    {
        surface.Dispose();
        releasedSurfaces++;
    }
}
