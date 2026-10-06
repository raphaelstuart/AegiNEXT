using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

/// <summary>不持有原生字形资源的排版 run 快照；基线与墨迹边界使用字幕局部坐标。</summary>
public sealed record SubtitleTextRunGeometry(int Utf16Start, int Utf16Length, int LineIndex,
    SubtitleStyle Style, SKPoint Baseline, SKRect Bounds)
{
    public string ResolvedFontFamily { get; init; } = string.Empty;
    public SubtitleFontVariant? ResolvedFontVariant { get; init; }
}
