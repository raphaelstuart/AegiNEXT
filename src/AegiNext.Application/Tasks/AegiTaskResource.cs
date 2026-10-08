namespace AegiNext.Application.Tasks;

/// <summary>An exclusive application resource, acquired with all other task resources.</summary>
public readonly record struct AegiTaskResource
{
    /// <summary>Creates a named resource identity.</summary>
    public AegiTaskResource(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    /// <summary>Gets the canonical identity of this resource.</summary>
    public string Key { get; }

    /// <summary>Creates an arbitrary shared resource identity.</summary>
    public static AegiTaskResource Named(string key) => new("named:" + key);

    /// <summary>Creates a project mutation resource.</summary>
    public static AegiTaskResource Project(string scopeId) => new("project:" + scopeId);

    /// <summary>Creates a media controller resource.</summary>
    public static AegiTaskResource Media(Guid controllerId) => new("media:" + controllerId.ToString("N"));

    /// <summary>Creates a preset library resource.</summary>
    public static AegiTaskResource PresetLibrary(string libraryId) => new("preset:" + libraryId);

    /// <summary>Creates a storage identity, resolving existing symbolic-link ancestors.</summary>
    public static AegiTaskResource StoragePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = NormalizeStoragePath(Path.GetFullPath(path), 0);
        var existingDirectory = Directory.Exists(normalized) ? normalized : Path.GetDirectoryName(normalized)!;
        while (!Directory.Exists(existingDirectory) && Path.GetDirectoryName(existingDirectory) is { } parent)
        {
            existingDirectory = parent;
        }

        var key = OperatingSystem.IsWindows() || IsCaseInsensitiveDirectory(existingDirectory)
            ? normalized.ToUpperInvariant()
            : normalized;
        return new("storage:" + key);
    }

    private static string NormalizeStoragePath(string fullPath, int linkDepth)
    {
        if (linkDepth > 40)
        {
            throw new IOException("The storage path contains too many symbolic link ancestors.");
        }

        var root = Path.GetPathRoot(fullPath)!;
        var ancestor = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(ancestor, segment);
            FileSystemInfo existing = Directory.Exists(candidate) ? new DirectoryInfo(candidate) : new FileInfo(candidate);
            if (existing.Exists)
            {
                if (existing.LinkTarget is not null)
                {
                    var target = existing.ResolveLinkTarget(true)?.FullName;
                    candidate = target is null ? candidate : NormalizeStoragePath(target, linkDepth + 1);
                }
                else
                {
                    candidate = CanonicalizeExistingName(ancestor, segment, candidate);
                }
            }

            ancestor = candidate;
        }

        return Path.TrimEndingDirectorySeparator(ancestor);
    }

    private static bool IsCaseInsensitiveDirectory(string directory)
    {
        var parent = Path.GetDirectoryName(directory);
        var name = Path.GetFileName(directory);
        if (parent is null || name.Length == 0)
        {
            return false;
        }

        var alternativeName = name.ToUpperInvariant();
        if (alternativeName == name)
        {
            alternativeName = name.ToLowerInvariant();
        }

        if (alternativeName == name)
        {
            return IsCaseInsensitiveDirectory(parent);
        }

        if (!Directory.Exists(Path.Combine(parent, alternativeName)))
        {
            return false;
        }

        try
        {
            return !Directory.EnumerateFileSystemEntries(parent).Any(entry =>
                string.Equals(Path.GetFileName(entry), alternativeName, StringComparison.Ordinal));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string CanonicalizeExistingName(string parent, string name, string fallback)
    {
        try
        {
            string? caseInsensitiveMatch = null;
            foreach (var entry in Directory.EnumerateFileSystemEntries(parent))
            {
                var existingName = Path.GetFileName(entry);
                if (string.Equals(existingName, name, StringComparison.Ordinal))
                {
                    return entry;
                }

                if (string.Equals(existingName, name, StringComparison.OrdinalIgnoreCase))
                {
                    caseInsensitiveMatch = entry;
                }
            }

            return caseInsensitiveMatch ?? fallback;
        }
        catch (IOException)
        {
            return fallback;
        }
        catch (UnauthorizedAccessException)
        {
            return fallback;
        }
    }

}
