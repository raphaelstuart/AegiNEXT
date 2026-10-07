using Avalonia;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private double mediaDuration;
    private SolidColorBrush mediaRangeBrush = new();

    /// <summary>同步实际视频时长，仅刷新有效范围底色，不改变工程、视口或播放头。</summary>
    public void SetMediaDuration(double value)
    {
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }
        if (mediaDuration.Equals(value))
        {
            return;
        }

        mediaDuration = value;
        audioDrawing.Dispose();
        InvalidateVisual();
    }

    private void DrawMediaRangeFill(DrawingContext context, Rect body)
    {
        var start = Math.Max(0, ViewStart);
        var end = Math.Min(mediaDuration, ViewStart + VisibleDuration);
        if (end <= start || body.Width <= 0 || body.Height <= 0 || mediaRangeBrush.Color.A == 0)
        {
            return;
        }

        var rectangle = new Rect(X(start), body.Top, (end - start) * PixelsPerSecond, body.Height).Intersect(body);
        if (rectangle.Width > 0 && rectangle.Height > 0)
        {
            context.DrawRectangle(mediaRangeBrush, null, rectangle);
        }
    }
}
