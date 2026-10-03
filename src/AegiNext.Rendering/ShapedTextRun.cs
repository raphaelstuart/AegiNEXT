using SkiaSharp;

namespace AegiNext.Rendering;

/// <summary>
/// 拥有已塑形文字的不可变绘制资源，可在 TextShaper 释放后继续使用；调用方负责释放。
/// </summary>
public sealed class ShapedTextRun : IDisposable
{
    private readonly SKTextBlob blob;
    private readonly ShapedGlyph[] glyphs;
    private bool isDisposed;

    internal ShapedTextRun(SKTextBlob blob, ShapedGlyph[] glyphs, float advanceWidth)
    {
        this.blob = blob;
        this.glyphs = glyphs;
        AdvanceWidth = advanceWidth;
    }

    public float AdvanceWidth { get; }
    public ReadOnlySpan<ShapedGlyph> Glyphs => glyphs;

    /// <inheritdoc />
    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        blob.Dispose();
        isDisposed = true;
    }

    internal SKTextBlob GetBlob()
    {
        ObjectDisposedException.ThrowIf(isDisposed, this);
        return blob;
    }
}
