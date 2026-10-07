using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AegiNext.Desktop.Controls;

internal sealed class TimelineDrawingCache : IDisposable
{
    internal const long MAX_CONTROL_BYTES = 32L * 1024 * 1024;
    private RenderTargetBitmap? bitmap;
    private Size size;
    private double scaling;

    internal long AllocatedBytes { get; private set; }

    internal static bool CanCache(Size size, double scaling, int layerCount)
    {
        var width = Math.Ceiling(size.Width * scaling);
        var height = Math.Ceiling(size.Height * scaling);
        return layerCount > 0 && size.Width > 0 && size.Height > 0 && double.IsFinite(scaling) && scaling > 0 &&
            width <= int.MaxValue && height <= int.MaxValue && width * height * 4 * layerCount <= MAX_CONTROL_BYTES;
    }

    internal void Draw(DrawingContext context, Size requestedSize, double requestedScaling, bool cacheEnabled,
        Action<DrawingContext> draw)
    {
        if (!cacheEnabled)
        {
            Dispose();
            draw(context);
            return;
        }
        if (bitmap is null || size != requestedSize || !scaling.Equals(requestedScaling))
        {
            Dispose();
            size = requestedSize;
            scaling = requestedScaling;
            var pixels = new PixelSize((int)Math.Ceiling(size.Width * scaling), (int)Math.Ceiling(size.Height * scaling));
            var next = new RenderTargetBitmap(pixels, new(96 * scaling, 96 * scaling));
            try
            {
                using (var drawing = next.CreateDrawingContext())
                {
                    draw(drawing);
                }
                bitmap = next;
                AllocatedBytes = (long)pixels.Width * pixels.Height * 4;
            }
            catch
            {
                next.Dispose();
                throw;
            }
        }
        context.DrawImage(bitmap, new Rect(requestedSize));
    }

    public void Dispose()
    {
        bitmap?.Dispose();
        bitmap = null;
        AllocatedBytes = 0;
    }
}
