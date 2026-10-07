using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Tests;

internal sealed class GeneratedImageProjectAssetResolver : IProjectAssetResolver
{
    private readonly byte[] pixels;

    internal GeneratedImageProjectAssetResolver()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(255, 128, 32));
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        pixels = encoded.ToArray();
    }

    /// <inheritdoc />
    public Stream Open(ProjectAsset asset)
    {
        return new MemoryStream(pixels, false);
    }
}
