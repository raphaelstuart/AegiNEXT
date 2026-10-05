using SkiaSharp;

namespace AegiNext.Rendering.Projects;

internal sealed class SubtitleLayout(IReadOnlyList<SubtitleLayoutLine> lines, SKRect bounds, SKPoint basePosition,
    SKPoint pivot, bool hasInk, SubtitleTextLayout snapshot) : IDisposable
{
    internal IReadOnlyList<SubtitleLayoutLine> Lines { get; } = lines;
    internal SKRect Bounds { get; } = bounds;
    internal SKPoint BasePosition { get; } = basePosition;
    internal SKPoint Pivot { get; } = pivot;
    internal bool HasInk { get; } = hasInk;
    internal SubtitleTextLayout Snapshot { get; } = snapshot;

    public void Dispose()
    {
        foreach (var line in Lines)
        {
            foreach (var run in line.Runs)
            {
                run.Shape.Dispose();
            }
        }
    }
}
