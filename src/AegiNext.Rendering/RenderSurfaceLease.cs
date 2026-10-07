namespace AegiNext.Rendering;

internal sealed class RenderSurfaceLease : IDisposable
{
    private readonly LinearRenderSurface surface;
    private RenderSurfacePool? owner;

    internal RenderSurfaceLease(RenderSurfacePool owner, LinearRenderSurface surface)
    {
        this.owner = owner;
        this.surface = surface;
    }

    internal LinearRenderSurface Surface
    {
        get
        {
            ObjectDisposedException.ThrowIf(owner is null, this);
            return surface;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var pool = owner;
        if (pool is null)
        {
            return;
        }

        owner = null;
        pool.Return(surface);
    }
}
