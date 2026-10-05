namespace AegiNext.Desktop.Editing;

/// <summary>时间线唯一的时间缩放、平移与轨道滚动状态，不持有呈现资源。</summary>
public sealed record TimelineViewport(double StartSeconds = 0, double PixelsPerSecond = 48,
    double VerticalOffset = 0, double Width = 0, double Height = 0)
{
    internal const double MAX_PIXELS_PER_SECOND = 20000;
    public double VisibleDuration => Width / PixelsPerSecond;

    /// <summary>将视口限制到有限工程范围和可滚动轨道范围。</summary>
    public TimelineViewport Normalize(double duration, double contentHeight)
    {
        var width = Finite(Width, 0, double.MaxValue, 0);
        var height = Finite(Height, 0, double.MaxValue, 0);
        var extent = Finite(duration, 0.001, double.MaxValue, 1);
        var scale = Finite(PixelsPerSecond, Math.Min(0.01, width / extent), MAX_PIXELS_PER_SECOND, 48);
        scale = Math.Max(0.000001, scale);
        return this with
        {
            Width = width,
            Height = height,
            PixelsPerSecond = scale,
            StartSeconds = Finite(StartSeconds, 0, Math.Max(0, extent - width / scale), 0),
            VerticalOffset = Finite(VerticalOffset, 0, Math.Max(0, contentHeight - height), 0)
        };
    }

    /// <summary>同步呈现尺寸并保留时间起点与缩放，允许扩大视口后显示工程末尾空白。</summary>
    public TimelineViewport Resize(double width, double height, double duration, double contentHeight)
    {
        var resized = (this with { Width = width, Height = height }).Normalize(duration, contentHeight);
        return resized with { StartSeconds = Finite(StartSeconds, 0, double.MaxValue, 0) };
    }

    /// <summary>缩放时保持指针下的工程时间，边界处按工程范围钳制。</summary>
    public TimelineViewport ZoomAt(double factor, double pointerX, double duration, double contentHeight)
    {
        if (!double.IsFinite(factor) || factor <= 0)
        {
            return this;
        }

        var x = Math.Clamp(pointerX, 0, Width);
        var time = StartSeconds + x / PixelsPerSecond;
        var scaled = (this with { PixelsPerSecond = PixelsPerSecond * factor }).Normalize(duration, contentHeight);
        return (scaled with { StartSeconds = time - x / scaled.PixelsPerSecond }).Normalize(duration, contentHeight);
    }

    /// <summary>以呈现像素平移时间和轨道位置。</summary>
    public TimelineViewport Pan(double horizontalPixels, double verticalPixels, double duration, double contentHeight)
    {
        return (this with
        {
            StartSeconds = StartSeconds + horizontalPixels / PixelsPerSecond,
            VerticalOffset = VerticalOffset + verticalPixels
        }).Normalize(duration, contentHeight);
    }

    private static double Finite(double value, double minimum, double maximum, double fallback)
    {
        return double.IsFinite(value) ? Math.Clamp(value, minimum, maximum) : fallback;
    }
}
