using System.Collections.Immutable;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleLayoutRun(string Text, int Utf16Offset, SubtitleStyle Style, ShapedTextRun Shape,
    TextDirection Direction, SKPoint Position = default)
{
    internal string ResolvedFontFamily { get; init; } = string.Empty;
    internal ImmutableArray<SubtitleGraphemeGeometry> Graphemes { get; init; } = [];
    internal ImmutableArray<SubtitleKaraokeSpan> Karaoke { get; init; } = [];
}
