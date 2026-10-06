using AegiNext.Media.Analysis;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private void DrawSpectrogram(DrawingContext context, Rect body, SpectrogramData? data, WriteableBitmap? bitmap)
    {
        if (data is null || bitmap is null)
        {
            return;
        }
        var start = Math.Max(Math.Max(0, ViewStart), Seconds(data.Start));
        var end = Math.Min(ViewStart + VisibleDuration, Seconds(data.End));
        if (waveformMediaDuration is { } mediaDuration)
        {
            end = Math.Min(end, Seconds(mediaDuration));
        }
        if (end <= start)
        {
            return;
        }
        var step = Seconds(data.ColumnDuration);
        var left = (start - Seconds(data.Start)) / step;
        var columns = (end - start) / step;
        context.DrawImage(bitmap, new Rect(left, 0, columns, data.Height),
            new Rect(X(start), body.Top, (end - start) * PixelsPerSecond, body.Height));
    }
}
