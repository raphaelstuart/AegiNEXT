using SkiaSharp;

namespace AegiNext.Rendering;

/// <summary>
/// 拥有已塑形文字的不可变绘制资源，可在 TextShaper 释放后继续使用；调用方负责释放。
/// </summary>
public sealed class ShapedTextRun : IDisposable
{
    private readonly SKTextBlob blob;
    private readonly ShapedGlyph[] glyphs;
    private readonly ShapedTextCluster[] clusters;
    private bool isDisposed;

    internal ShapedTextRun(SKTextBlob blob, ShapedGlyph[] glyphs, ShapedTextCluster[] clusters,
        float advanceWidth, SKRect inkBounds, SKFontMetrics metrics)
    {
        this.blob = blob;
        this.glyphs = glyphs;
        this.clusters = clusters;
        AdvanceWidth = advanceWidth;
        InkBounds = inkBounds;
        FontMetrics = metrics;
    }

    public float AdvanceWidth { get; }
    public SKRect InkBounds { get; }
    public SKFontMetrics FontMetrics { get; }
    public ReadOnlySpan<ShapedGlyph> Glyphs => glyphs;
    public ReadOnlySpan<ShapedTextCluster> Clusters => clusters;

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
