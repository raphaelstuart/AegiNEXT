namespace AegiNext.Rendering.Projects;

internal sealed class SubtitleLayout(IReadOnlyList<SubtitleLayoutLine> lines) : IDisposable
{
    internal IReadOnlyList<SubtitleLayoutLine> Lines { get; } = lines;

    public void Dispose()
    {
        foreach (var line in Lines)
        {
            line.Run?.Dispose();
        }
    }
}
