using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

internal sealed class FontNamePreviewTestFace(FontNamePreviewTestRenderer renderer, string fingerprint) : IFontNamePreviewFace
{
    /// <inheritdoc />
    public string Fingerprint { get; } = fingerprint;

    /// <inheritdoc />
    public FontNamePreview? Render(FontNamePreviewRequest request, CancellationToken cancellationToken) =>
        renderer.Render(request, cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        renderer.OnFaceDisposed();
    }
}
