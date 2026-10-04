using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed class SubtitleLayout(IReadOnlyList<SubtitleLayoutLine> lines, SKRect bounds, SKPoint basePosition,
    SKPoint pivot, bool hasInk) : IDisposable
{
    internal IReadOnlyList<SubtitleLayoutLine> Lines { get; } = lines;
    internal SKRect Bounds { get; } = bounds;
    internal SKPoint BasePosition { get; } = basePosition;
    internal SKPoint Pivot { get; } = pivot;
    internal bool HasInk { get; } = hasInk;

    public void Dispose()
    {
        foreach (var line in Lines)
        {
            line.Run?.Dispose();
        }
    }
}
