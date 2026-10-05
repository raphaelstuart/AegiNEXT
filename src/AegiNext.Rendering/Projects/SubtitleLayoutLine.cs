using SkiaSharp;
using System.Collections.Immutable;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleLayoutLine(string Text, int Utf16Offset, ImmutableArray<SubtitleLayoutRun> Runs,
    float FontSize, float AdvanceWidth, SKPoint Position = default);
