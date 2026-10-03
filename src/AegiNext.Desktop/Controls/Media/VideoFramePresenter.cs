using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Controls;

/// <summary>持有并释放视频预览位图的呈现控件，重挂到其他宿主时保留当前画面。</summary>
public sealed class VideoFramePresenter : Image, IDisposable
{
    private WriteableBitmap? bitmap;
    private bool disposed;

    /// <summary>呈现已由播放控制器验证请求身份的帧。</summary>
    public unsafe void Present(SdrVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (bitmap is null || bitmap.PixelSize.Width != frame.Width || bitmap.PixelSize.Height != frame.Height)
        {
            var previous = bitmap;
            bitmap = new(new(frame.Width, frame.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            Source = bitmap;
            previous?.Dispose();
        }

        using (var target = bitmap.Lock())
        {
            var rowBytes = checked(frame.Width * 4);
            for (var row = 0; row < frame.Height; row++)
            {
                frame.Pixels.Span.Slice(row * rowBytes, rowBytes)
                    .CopyTo(new Span<byte>((void*)(target.Address + row * target.RowBytes), rowBytes));
            }
        }

        InvalidateVisual();
    }

    /// <summary>清空并释放当前预览，不影响播放会话。</summary>
    public void Clear()
    {
        Source = null;
        bitmap?.Dispose();
        bitmap = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Clear();
        }
    }
}
