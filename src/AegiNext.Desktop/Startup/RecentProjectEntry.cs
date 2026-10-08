namespace AegiNext.Desktop.Startup;

internal sealed record RecentProjectEntry(string Path, string Name, DateTimeOffset LastUsedUtc, bool IsPinned = false);
