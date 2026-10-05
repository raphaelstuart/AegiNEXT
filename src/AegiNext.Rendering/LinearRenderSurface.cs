using System.Numerics;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace AegiNext.Rendering;

/// <summary>
/// 拥有 CPU 离屏 F16 表面的绘制上下文。实例限单线程使用，调用方负责释放。
/// </summary>
public sealed class LinearRenderSurface : IDisposable
{
    private readonly SKColorSpace colorSpace;
    private readonly SKSurface surface;
    private bool isDisposed;

    /// <summary>
    /// 创建透明黑表面；不创建窗口，也不执行显示器映射或 tone mapping。
    /// </summary>
    public LinearRenderSurface(RenderSurfaceInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Info = info;
        colorSpace = SKColorSpace.CreateSrgbLinear();
        SKSurface? createdSurface = null;
        try
        {
            createdSurface = SKSurface.Create(
                new SKImageInfo(info.Width, info.Height, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace), info.RowBytes)
                ?? throw new InvalidOperationException("无法创建 F16 离屏表面。");
            createdSurface.Canvas.Clear(SKColors.Transparent);
            surface = createdSurface;
        }
        catch
        {
            createdSurface?.Dispose();
            colorSpace.Dispose();
            throw;
        }
    }

    public RenderSurfaceInfo Info { get; }

    internal SKCanvas Canvas
    {
        get
        {
            ObjectDisposedException.ThrowIf(isDisposed, this);
            return surface.Canvas;
        }
    }

    internal SKColorSpace ColorSpace => colorSpace;

    internal SKImage Snapshot()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return surface.Snapshot();
    }

    internal void ReplacePixels(ReadOnlySpan<Half> pixels)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        using var image = SKImage.FromPixelCopy(new(Info.Width, Info.Height, SKColorType.RgbaF16, SKAlphaType.Premul, colorSpace),
            MemoryMarshal.AsBytes(pixels), Info.RowBytes) ?? throw new InvalidOperationException("无法替换 F16 像素。");
        using var paint = new SKPaint { BlendMode = SKBlendMode.Src };
        var saved = surface.Canvas.Save();
        try
        {
            surface.Canvas.ResetMatrix();
            surface.Canvas.DrawImage(image, 0, 0, paint);
        }
        finally
        {
            surface.Canvas.RestoreToCount(saved);
        }
    }

    /// <summary>复制紧密排列的预乘 sRGB BGRA 像素，保留透明度；调用方拥有返回的缓冲。</summary>
    public byte[] CopySrgbBgra()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        using var srgb = SKColorSpace.CreateSrgb();
        using var bitmap = new SKBitmap(new SKImageInfo(Info.Width, Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul, srgb));
        if (!surface.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0))
        {
            throw new InvalidOperationException("无法读取 sRGB 预览像素。");
        }

        var result = new byte[checked(Info.Width * Info.Height * 4)];
        for (var row = 0; row < Info.Height; row++)
        {
            Marshal.Copy(bitmap.GetPixels() + row * bitmap.RowBytes, result, row * Info.Width * 4, Info.Width * 4);
        }

        return result;
    }

    /// <summary>
    /// 清除为透明黑，可供下一帧复用。
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        surface.Canvas.Clear(SKColors.Transparent);
    }

    /// <summary>
    /// 按未预乘线性颜色及 source-over 规则填充矩形，坐标单位为像素。
    /// </summary>
    public void FillRectangle(Vector2 origin, Vector2 size, LinearColor color)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var bounds = GetBounds(origin, size);
        using var paint = CreatePaint(color);
        surface.Canvas.DrawRect(bounds, paint);
    }

    /// <summary>
    /// 以灰度抗锯齿填充指定边界内的椭圆。
    /// </summary>
    public void FillEllipse(Vector2 origin, Vector2 size, LinearColor color)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        var bounds = GetBounds(origin, size);
        using var paint = CreatePaint(color);
        surface.Canvas.DrawOval(bounds, paint);
    }

    /// <summary>
    /// 在指定基线原点绘制已塑形文字；不接管文字资源所有权。
    /// </summary>
    public void DrawText(ShapedTextRun text, Vector2 baselineOrigin, LinearColor color)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(text);
        RenderValidation.Point(baselineOrigin, nameof(baselineOrigin));
        using var paint = CreatePaint(color);
        surface.Canvas.DrawText(text.GetBlob(), baselineOrigin.X, baselineOrigin.Y, paint);
    }

    /// <summary>
    /// 以 source-over 合成源表面的快照；透明度作用于整体图层，两个表面必须使用相同亮度标度。
    /// </summary>
    public void Composite(LinearRenderSurface source, Vector2 origin, float opacity)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ObjectDisposedException.ThrowIf(source.isDisposed, source);
        RenderValidation.Point(origin, nameof(origin));
        RenderValidation.UnitInterval(opacity, nameof(opacity));
        if (!source.Info.ReferenceWhiteNits.Equals(Info.ReferenceWhiteNits))
        {
            throw new ArgumentException("合成前必须统一参考白亮度标度。", nameof(source));
        }

        using var snapshot = source.surface.Snapshot();
        using var paint = CreatePaint(new(1, 1, 1, opacity));
        surface.Canvas.DrawImage(snapshot, origin.X, origin.Y, new SKSamplingOptions(SKFilterMode.Linear), paint);
    }

    /// <summary>
    /// 将原始预乘 RGBA Half 像素逐行复制到调用方缓冲；上到下紧密排列，不解预乘或转换为 8 位。
    /// </summary>
    public void CopyPixels(Span<Half> destination)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        if (destination.Length < Info.ChannelCount)
        {
            throw new ArgumentException("目标缓冲不足以容纳全部像素。", nameof(destination));
        }

        using var pixels = surface.PeekPixels()
            ?? throw new InvalidOperationException("无法读取离屏像素。");
        var bytes = pixels.GetPixelSpan();
        var rowChannels = Info.Width * 4;
        for (var row = 0; row < Info.Height; row++)
        {
            MemoryMarshal.Cast<byte, Half>(bytes.Slice(row * pixels.RowBytes, Info.RowBytes))
                .CopyTo(destination.Slice(row * rowChannels, rowChannels));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        surface.Dispose();
        colorSpace.Dispose();
        isDisposed = true;
    }

    private SKPaint CreatePaint(LinearColor color)
    {
        var paint = new SKPaint();
        try
        {
            paint.IsAntialias = true;
            paint.BlendMode = SKBlendMode.SrcOver;
            paint.SetColor(new(color.Red, color.Green, color.Blue, color.Alpha), colorSpace);
            return paint;
        }
        catch
        {
            paint.Dispose();
            throw;
        }
    }

    private static SKRect GetBounds(Vector2 origin, Vector2 size)
    {
        RenderValidation.Point(origin, nameof(origin));
        RenderValidation.Point(size, nameof(size));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.X, nameof(size));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size.Y, nameof(size));
        var end = origin + size;
        RenderValidation.Point(end, nameof(size));
        return new(origin.X, origin.Y, end.X, end.Y);
    }
}
