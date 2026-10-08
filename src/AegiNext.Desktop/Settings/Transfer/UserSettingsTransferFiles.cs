namespace AegiNext.Desktop.Settings.Transfer;

internal static class UserSettingsTransferFiles
{
    private const int BUFFER_SIZE = 65536;
    private const int MAXIMUM_LINK_DEPTH = 40;

    internal static string ResolvePhysicalPath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var links = 0;
        return ResolvePhysicalPath(Path.GetFullPath(path), ref links);
    }

    private static string ResolvePhysicalPath(string fullPath, ref int links)
    {
        var parent = Path.GetDirectoryName(fullPath);
        if (parent is null)
        {
            return fullPath;
        }
        var resolved = Path.Combine(ResolvePhysicalPath(parent, ref links), Path.GetFileName(fullPath));
        FileSystemInfo entry = Directory.Exists(resolved) ? new DirectoryInfo(resolved) : new FileInfo(resolved);
        if (entry.LinkTarget is not null)
        {
            links++;
            if (links > MAXIMUM_LINK_DEPTH)
            {
                throw new InvalidDataException("用户设置目录的链接层数超过限制。");
            }
            var target = entry.ResolveLinkTarget(false) ?? throw new InvalidDataException("用户设置目录的链接目标无效。");
            return ResolvePhysicalPath(target.FullName, ref links);
        }
        return resolved;
    }

    internal static async Task<byte[]> ReadAsync(string path, int maximumBytes, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            BUFFER_SIZE, FileOptions.Asynchronous);
        RequireLength(stream.Length, maximumBytes);
        using var buffer = new MemoryStream(checked((int)stream.Length));
        var chunk = new byte[BUFFER_SIZE];
        var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
        while (read > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireLength(buffer.Length + read, maximumBytes);
            buffer.Write(chunk, 0, read);
            read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return buffer.ToArray();
    }

    internal static byte[] Read(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Read(stream, maximumBytes);
    }

    internal static byte[] Read(Stream stream, int maximumBytes, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[BUFFER_SIZE];
        var read = stream.Read(chunk, 0, chunk.Length);
        while (read > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequireLength(buffer.Length + read, maximumBytes);
            buffer.Write(chunk, 0, read);
            read = stream.Read(chunk, 0, chunk.Length);
        }
        return buffer.ToArray();
    }

    internal static Task WriteAtomicAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        return WriteAtomicAsync(path, bytes, null, cancellationToken);
    }

    internal static async Task WriteAtomicAsync(string path, ReadOnlyMemory<byte> bytes, Action? beforeCommit,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             BUFFER_SIZE, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(true);
            }
            cancellationToken.ThrowIfCancellationRequested();
            beforeCommit?.Invoke();
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    internal static void WriteAtomic(string path, ReadOnlySpan<byte> bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                       BUFFER_SIZE, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(true);
            }
            File.Move(temporary, path, true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void RequireLength(long length, int maximumBytes)
    {
        if (length > maximumBytes)
        {
            throw new InvalidDataException("用户设置文件超过容量限制。");
        }
    }
}
