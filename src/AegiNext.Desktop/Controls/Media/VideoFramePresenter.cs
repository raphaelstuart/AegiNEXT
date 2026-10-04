using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Controls;

/// <summary>持有并释放视频预览位图的呈现控件，重挂到其他宿主时保留当前画面。</summary>
public sealed class VideoFramePresenter : Image, IDisposable
{
    private readonly VideoFrameSurface surface = new();
    private bool disposed;

    /// <summary>呈现已由播放控制器验证请求身份的帧。</summary>
    public void Present(SdrVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        surface.Present(frame);
        Source = surface.Bitmap;
        InvalidateVisual();
    }

    /// <summary>清空并释放当前预览，不影响播放会话。</summary>
    public void Clear()
    {
        Source = null;
        surface.Clear();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            Clear();
            surface.Dispose();
        }
    }
}
