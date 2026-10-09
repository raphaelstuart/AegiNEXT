using AegiNext.Rendering.Fonts;

namespace AegiNext.Desktop.Tests;

internal sealed class FontNamePreviewTestRenderer : IFontNamePreviewRenderer
{
    private int resolveCount;
    private int renderCount;
    private int disposedFaceCount;

    internal string Fingerprint { get; set; } = "font-name-alpha8-v1:fixture-font-a";
    internal int ResolveCount => Volatile.Read(ref resolveCount);
    internal int RenderCount => Volatile.Read(ref renderCount);
    internal int DisposedFaceCount => Volatile.Read(ref disposedFaceCount);
    internal Func<FontNamePreviewRequest, CancellationToken, FontNamePreview?>? RenderHandler { get; set; }

    /// <inheritdoc />
    public IFontNamePreviewFace? Resolve(FontNamePreviewRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Interlocked.Increment(ref resolveCount);
        return new FontNamePreviewTestFace(this, Fingerprint);
    }

    internal FontNamePreview? Render(FontNamePreviewRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref renderCount);
        cancellationToken.ThrowIfCancellationRequested();
        return RenderHandler is { } render
            ? render(request, cancellationToken)
            : new(4, 2, 4 / request.RenderScale, 2 / request.RenderScale, new byte[] { 0, 10, 100, 255, 255, 100, 10, 0 });
    }

    internal void OnFaceDisposed()
    {
        Interlocked.Increment(ref disposedFaceCount);
    }
}
