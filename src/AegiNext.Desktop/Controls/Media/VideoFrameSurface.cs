using AegiNext.Media.Preview;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace AegiNext.Desktop.Controls;

internal sealed class VideoFrameSurface : IDisposable
{
    private bool disposed;
    internal WriteableBitmap? Bitmap { get; private set; }

    internal unsafe void Present(SdrVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (Bitmap is null || Bitmap.PixelSize.Width != frame.Width || Bitmap.PixelSize.Height != frame.Height)
        {
            var previous = Bitmap;
            Bitmap = new(new(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            previous?.Dispose();
        }

        using var target = Bitmap.Lock();
        var rowBytes = checked(frame.Width * 4);
        for (var row = 0; row < frame.Height; row++)
        {
            frame.Pixels.Span.Slice(row * rowBytes, rowBytes)
                .CopyTo(new Span<byte>((void*)(target.Address + row * target.RowBytes), rowBytes));
        }
    }

    internal void Clear()
    {
        Bitmap?.Dispose();
        Bitmap = null;
    }

    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Clear();
        }
    }
}
