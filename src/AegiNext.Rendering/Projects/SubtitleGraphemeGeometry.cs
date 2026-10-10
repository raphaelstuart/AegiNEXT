using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>完整字素的逻辑选区及两侧光标；连字内部按字素分配同一 cluster 的推进宽度。</summary>
public sealed record SubtitleGraphemeGeometry(int Utf16Start, int Utf16Length, int LineIndex,
    SKRect Bounds, SKRect LeadingCaret, SKRect TrailingCaret)
{
    public SKMatrix LocalToVisible { get; init; } = SKMatrix.Identity;
    public SKRect? UntransformedBounds { get; init; }
}
