using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace AegiNext.Desktop.Controls;

internal sealed class BezierCanvasCursors : IDisposable
{
    private readonly Bitmap insertionBitmap = CreateBitmap(true);
    private readonly Bitmap deletionBitmap = CreateBitmap(false);
    internal Cursor Cross { get; } = new(StandardCursorType.Cross);
    internal Cursor Insert { get; }
    internal Cursor Delete { get; }

    internal BezierCanvasCursors()
    {
        Insert = new(insertionBitmap, new PixelPoint(9, 9));
        Delete = new(deletionBitmap, new PixelPoint(9, 9));
    }

    internal static Bitmap CreateBitmap(bool insertion)
    {
        using var bitmap = new SKBitmap(32, 32, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var path = new SKPath();
        path.MoveTo(9, 1);
        path.LineTo(9, 6);
        path.MoveTo(9, 12);
        path.LineTo(9, 17);
        path.MoveTo(1, 9);
        path.LineTo(6, 9);
        path.MoveTo(12, 9);
        path.LineTo(17, 9);
        path.MoveTo(18, 23);
        path.LineTo(28, 23);
        if (insertion)
        {
            path.MoveTo(23, 18);
            path.LineTo(23, 28);
        }
        using var outline = new SKPaint { IsAntialias = true, Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 4, StrokeCap = SKStrokeCap.Round };
        using var foreground = new SKPaint { IsAntialias = true, Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 2, StrokeCap = SKStrokeCap.Round };
        canvas.DrawPath(path, outline);
        canvas.DrawPath(path, foreground);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = data.AsStream();
        return new(stream);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Cross.Dispose();
        Insert.Dispose();
        Delete.Dispose();
        insertionBitmap.Dispose();
        deletionBitmap.Dispose();
    }
}
