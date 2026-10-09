using System.Buffers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

internal sealed class SystemFontNamePreviewFace : IFontNamePreviewFace
{
    private const string RENDER_SCHEMA = "font-name-alpha8-v1";
    private const int HASH_BUFFER_BYTES = 64 * 1024;
    private const int MAXIMUM_RASTER_DIMENSION = 32768;
    private const int MAXIMUM_RASTER_PIXELS = 4 * 1024 * 1024;
    private const int PIXEL_PADDING = 1;
    private readonly TextShaper shaper;
    private bool disposed;

    private SystemFontNamePreviewFace(TextShaper shaper, string fingerprint)
    {
        this.shaper = shaper;
        Fingerprint = fingerprint;
    }

    public string Fingerprint { get; }

    internal static SystemFontNamePreviewFace? TryCreate(SKTypeface ownedTypeface, SystemFontFace face,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(ownedTypeface);
        ArgumentNullException.ThrowIfNull(face);
        string? fingerprint;
        try
        {
            token.ThrowIfCancellationRequested();
            fingerprint = CreateFingerprint(ownedTypeface, face, token);
        }
        catch
        {
            ownedTypeface.Dispose();
            throw;
        }

        if (fingerprint is null)
        {
            ownedTypeface.Dispose();
            return null;
        }

        return new(new TextShaper(ownedTypeface, italic: face.Variant.Italic, face: face, explicitVariant: true),
            fingerprint);
    }

    /// <inheritdoc />
    public FontNamePreview? Render(FontNamePreviewRequest request, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        request.Validate();
        var pixelFontSize = request.FontSize * request.RenderScale;
        var maximumWidth = (int)Math.Min(MAXIMUM_RASTER_DIMENSION, Math.Floor(request.MaxWidth * request.RenderScale));
        if (!double.IsFinite(pixelFontSize) || pixelFontSize > MAXIMUM_RASTER_DIMENSION ||
            maximumWidth <= PIXEL_PADDING * 2 || !float.IsNormal((float)pixelFontSize) ||
            !shaper.ContainsGlyphs(request.Text))
        {
            return null;
        }

        var direction = request.Text.EnumerateRunes().Any(rune => rune.Value is >= 0x0590 and <= 0x08ff)
            ? TextDirection.RIGHT_TO_LEFT
            : TextDirection.LEFT_TO_RIGHT;
        using var shape = shaper.Shape(request.Text, (float)pixelFontSize, direction, "und");
        token.ThrowIfCancellationRequested();
        var left = Math.Min(0, shape.InkBounds.Left);
        var right = Math.Max(shape.AdvanceWidth, shape.InkBounds.Right);
        var top = Math.Min(shape.FontMetrics.Ascent, shape.InkBounds.Top);
        var bottom = Math.Max(shape.FontMetrics.Descent + Math.Max(0, shape.FontMetrics.Leading),
            shape.InkBounds.Bottom);
        var width = right - left;
        var height = bottom - top;
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var scale = Math.Min(1, (maximumWidth - PIXEL_PADDING * 2) / width);
        var pixelWidth = Math.Min(maximumWidth, (int)Math.Ceiling(width * scale + PIXEL_PADDING * 2));
        var requestedHeight = Math.Ceiling(height * scale + PIXEL_PADDING * 2);
        if (requestedHeight > MAXIMUM_RASTER_DIMENSION || pixelWidth * requestedHeight > MAXIMUM_RASTER_PIXELS)
        {
            return null;
        }

        var pixelHeight = (int)requestedHeight;
        using var bitmap =
            new SKBitmap(new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(bitmap);
        using var paint = new SKPaint();
        paint.Color = SKColors.White;
        paint.IsAntialias = true;
        canvas.Clear(SKColors.Transparent);
        canvas.Translate(PIXEL_PADDING, PIXEL_PADDING);
        canvas.Scale(scale);
        canvas.DrawText(shape.GetBlob(), -left, -top, paint);
        canvas.Flush();
        var pixels = bitmap.GetPixelSpan();
        var alpha = new byte[pixelWidth * pixelHeight];
        for (var row = 0; row < pixelHeight; row++)
        {
            token.ThrowIfCancellationRequested();
            for (var column = 0; column < pixelWidth; column++)
            {
                alpha[row * pixelWidth + column] = pixels[row * bitmap.RowBytes + column * 4 + 3];
            }
        }

        return new(pixelWidth, pixelHeight, pixelWidth / request.RenderScale, pixelHeight / request.RenderScale, alpha);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        shaper.Dispose();
    }

    private static string? CreateFingerprint(SKTypeface typeface, SystemFontFace face, CancellationToken token)
    {
        using var stream = typeface.OpenStream(out var fontIndex);
        if (stream is null)
        {
            return null;
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = ArrayPool<byte>.Shared.Rent(HASH_BUFFER_BYTES);
        var length = 0L;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, HASH_BUFFER_BYTES);
                if (read <= 0)
                {
                    break;
                }

                hash.AppendData(buffer.AsSpan(0, read));
                length += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        if (length == 0 || stream.HasLength && length != stream.Length)
        {
            return null;
        }

        var contentHash = Convert.ToHexString(hash.GetHashAndReset());
        token.ThrowIfCancellationRequested();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = RENDER_SCHEMA,
            SkiaVersion = typeof(SKTypeface).Assembly.GetName().Version?.ToString(),
            HarfBuzzVersion = typeof(HarfBuzzSharp.Font).Assembly.GetName().Version?.ToString(),
            OperatingSystem = RuntimeInformation.OSDescription,
            Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            ContentHash = contentHash,
            FontIndex = fontIndex,
            face.FamilyName,
            face.SourceFamilyName,
            face.IsVariable,
            face.CollectionIndex,
            face.NamedInstanceIndex,
            face.Variant
        })));
    }
}
