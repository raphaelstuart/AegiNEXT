using System.Buffers;
using System.Numerics;
using System.Text;
using HarfBuzzSharp;
using SkiaSharp;
using SkiaSharp.HarfBuzz;
using Buffer = HarfBuzzSharp.Buffer;

namespace AegiNext.Rendering;

/// <summary>
/// 使用调用方指定的字体塑形单行、单方向、单脚本文字；不做系统字体回退或段落布局。实例限单线程使用。
/// </summary>
public sealed class TextShaper : IDisposable
{
    private readonly SKTypeface typeface;
    private readonly SKShaper shaper;
    private bool isDisposed;

    internal TextShaper(SKTypeface ownedTypeface)
    {
        typeface = ownedTypeface;
        try
        {
            shaper = new(typeface);
        }
        catch
        {
            typeface.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 复制字体数据并打开指定字体 face；损坏字体会抛出异常，不静默回退到系统字体。
    /// </summary>
    public TextShaper(ReadOnlySpan<byte> fontData, int faceIndex = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(faceIndex);
        if (fontData.IsEmpty)
        {
            throw new ArgumentException("字体数据不能为空。", nameof(fontData));
        }

        using var data = SKData.CreateCopy(fontData);
        typeface = SKTypeface.FromData(data, faceIndex)
            ?? throw new ArgumentException("无法解析指定字体。", nameof(fontData));
        try
        {
            shaper = new(typeface);
        }
        catch
        {
            typeface.Dispose();
            throw;
        }
    }

    /// <summary>
    /// 塑形非空单行文本，显式指定像素字号、方向和语言；缺字抛出异常，cluster 使用 UTF-16 下标。
    /// </summary>
    public ShapedTextRun Shape(string text, float fontSize, TextDirection direction, string language)
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        ValidateText(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        RenderValidation.Finite(fontSize, nameof(fontSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fontSize);
        if (!float.IsNormal(fontSize))
        {
            throw new ArgumentOutOfRangeException(nameof(fontSize), "字号不能为会导致塑形缩放下溢的次正规数。");
        }

        if (!Enum.IsDefined(direction))
        {
            throw new ArgumentOutOfRangeException(nameof(direction));
        }

        using var buffer = new Buffer();
        buffer.AddUtf16(text);
        buffer.Direction = direction == TextDirection.LEFT_TO_RIGHT ? Direction.LeftToRight : Direction.RightToLeft;
        buffer.Language = new(language);
        buffer.GuessSegmentProperties();
        using var font = new SKFont(typeface, fontSize);
        font.Edging = SKFontEdging.Antialias;
        font.Hinting = SKFontHinting.None;
        font.Subpixel = true;
        var metrics = font.Metrics;
        RenderValidation.Finite(metrics.Top, nameof(fontSize));
        RenderValidation.Finite(metrics.Bottom, nameof(fontSize));
        RenderValidation.Finite(metrics.Ascent, nameof(fontSize));
        RenderValidation.Finite(metrics.Descent, nameof(fontSize));
        RenderValidation.Finite(metrics.Leading, nameof(fontSize));
        RenderValidation.Finite(metrics.MaxCharacterWidth, nameof(fontSize));
        var result = shaper.Shape(buffer, font);
        var ids = new ushort[result.Codepoints.Length];
        var glyphs = new ShapedGlyph[ids.Length];
        RenderValidation.Finite(result.Width, nameof(fontSize));
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = checked((ushort)result.Codepoints[i]);
            if (ids[i] == 0)
            {
                throw new InvalidOperationException($"字体缺少 UTF-16 cluster {result.Clusters[i]} 所需的字形。");
            }

            var point = new Vector2(result.Points[i].X, result.Points[i].Y);
            RenderValidation.Point(point, nameof(fontSize));
            glyphs[i] = new(ids[i], checked((int)result.Clusters[i]), point);
        }

        using var builder = new SKTextBlobBuilder();
        builder.AddPositionedRun(ids, font, result.Points);
        var blob = builder.Build() ?? throw new InvalidOperationException("文本未产生可绘制字形。");
        try
        {
            var bounds = blob.Bounds;
            RenderValidation.Finite(bounds.Left, nameof(fontSize));
            RenderValidation.Finite(bounds.Top, nameof(fontSize));
            RenderValidation.Finite(bounds.Right, nameof(fontSize));
            RenderValidation.Finite(bounds.Bottom, nameof(fontSize));
            _ = font.GetGlyphWidths(ids.AsSpan(), out var glyphBounds);
            var inkBounds = SKRect.Empty;
            for (var index = 0; index < glyphBounds.Length; index++)
            {
                var glyphBound = glyphBounds[index];
                if (glyphBound.IsEmpty)
                {
                    continue;
                }

                glyphBound.Offset(result.Points[index]);
                RenderValidation.Finite(glyphBound.Left, nameof(fontSize));
                RenderValidation.Finite(glyphBound.Top, nameof(fontSize));
                RenderValidation.Finite(glyphBound.Right, nameof(fontSize));
                RenderValidation.Finite(glyphBound.Bottom, nameof(fontSize));
                inkBounds = inkBounds.IsEmpty ? glyphBound : SKRect.Union(inkBounds, glyphBound);
            }

            return new(blob, glyphs, result.Width, inkBounds);
        }
        catch
        {
            blob.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        shaper.Dispose();
        typeface.Dispose();
        isDisposed = true;
    }

    private static void ValidateText(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            if (Rune.DecodeFromUtf16(remaining, out var rune, out var consumed) != OperationStatus.Done
                || Rune.IsControl(rune) || rune.Value is 0x2028 or 0x2029)
            {
                throw new ArgumentException("需要有效的单行 UTF-16 文本，不支持换行或控制字符。", nameof(text));
            }

            remaining = remaining[consumed..];
        }
    }
}
