using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleVisualFragment(SubtitleStyle Style, int Utf16Cluster, SKRect? InkClip,
    SKRect DecorationClip)
{
    internal SKMatrix Matrix { get; init; } = SKMatrix.Identity;
}
