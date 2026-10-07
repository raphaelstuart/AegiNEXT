using System.Collections.Immutable;

namespace AegiNext.Desktop.Workspace;

internal static class VideoFileTypes
{
    private static ImmutableArray<string> Extensions { get; } =
        [".mkv", ".mp4", ".mov", ".webm", ".avi", ".m4v", ".ts", ".m2ts"];

    internal static string[] Patterns => [.. Extensions.Select(extension => "*" + extension)];

    internal static bool SupportsPath(string path)
    {
        return Extensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
    }
}
