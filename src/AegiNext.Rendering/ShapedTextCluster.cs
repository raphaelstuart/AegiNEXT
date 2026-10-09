using SkiaSharp;

namespace AegiNext.Rendering;

/// <summary>塑形 cluster 的逻辑下标及有向水平范围，保留负字距后的光标顺序。</summary>
public readonly record struct ShapedTextCluster(int Utf16Start, float Start, float End)
{
    public SKRect InkBounds { get; init; }
    public float ContentEnd { get; init; }
}
