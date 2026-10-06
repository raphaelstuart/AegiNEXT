using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.ObjectModel;

namespace AegiNext.Desktop.Startup;

internal sealed class RecentProjectService : IAsyncDisposable
{
    private const int MAX_ENTRIES = 20;
    private const int MAX_FILE_BYTES = 65536;
    private static readonly StringComparer pathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;
    private static readonly JsonSerializerOptions jsonOptions = new()
    {
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };
    private readonly string storagePath;
    private readonly Lock stateGate = new();
    private readonly SemaphoreSlim writeGate = new(1, 1);
    private ReadOnlyCollection<RecentProjectEntry> entries = Array.AsReadOnly<RecentProjectEntry>([]);
    private Task completion = Task.CompletedTask;
    private Task? disposeTask;
    private bool disposing;

    internal RecentProjectService(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        storagePath = Path.Combine(Path.GetFullPath(directory), "recent-projects.json");
        Load();
    }

    internal event EventHandler? Changed;
    internal event EventHandler? ErrorChanged;
    internal Exception? LastError { get; private set; }

    internal IReadOnlyList<RecentProjectEntry> Entries
    {
        get
        {
            lock (stateGate)
            {
                return entries;
            }
        }
    }

    internal Task Completion
    {
        get
        {
            lock (stateGate)
            {
                return completion;
            }
        }
    }

    internal Task RecordAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Task write;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposing, this);
            var value = new RecentProjectEntry(fullPath, Path.GetFileNameWithoutExtension(fullPath), DateTimeOffset.UtcNow);
            entries = Array.AsReadOnly(entries.Where(entry => !pathComparer.Equals(entry.Path, fullPath))
                .Prepend(value).OrderByDescending(entry => entry.LastUsedUtc).Take(MAX_ENTRIES).ToArray());
            completion = PersistAsync(entries, completion);
            write = completion;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return write;
    }

    internal Task RemoveAsync(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        Task write;
        lock (stateGate)
        {
            ObjectDisposedException.ThrowIf(disposing, this);
            var remaining = entries.Where(entry => !pathComparer.Equals(entry.Path, fullPath)).ToArray();
            if (remaining.Length == entries.Count)
            {
                return completion;
            }

            entries = Array.AsReadOnly(remaining);
            completion = PersistAsync(entries, completion);
            write = completion;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return write;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        lock (stateGate)
        {
            disposing = true;
            disposeTask ??= DisposeCoreAsync(completion);
            return new(disposeTask);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(storagePath) && !Directory.Exists(storagePath))
            {
                return;
            }

            if (new FileInfo(storagePath).Length > MAX_FILE_BYTES)
            {
                throw new InvalidDataException("最近项目记录文件过大。");
            }

            var loaded = JsonSerializer.Deserialize<RecentProjectEntry[]>(File.ReadAllBytes(storagePath), jsonOptions)
                         ?? throw new InvalidDataException("最近项目记录为空。");
            var normalized = new List<RecentProjectEntry>(loaded.Length);
            foreach (var entry in loaded)
            {
                if (entry is null || string.IsNullOrWhiteSpace(entry.Path) ||
                    string.IsNullOrWhiteSpace(entry.Name) || entry.LastUsedUtc == default)
                {
                    throw new InvalidDataException("最近项目记录无效。");
                }

                var fullPath = Path.GetFullPath(entry.Path);
                normalized.Add(new(fullPath, Path.GetFileNameWithoutExtension(fullPath),
                    entry.LastUsedUtc.ToUniversalTime()));
            }

            entries = Array.AsReadOnly(normalized.OrderByDescending(entry => entry.LastUsedUtc)
                .DistinctBy(entry => entry.Path, pathComparer).Take(MAX_ENTRIES).ToArray());
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
                                          or ArgumentException or NotSupportedException)
        {
            LastError = error;
        }
    }

    private async Task PersistAsync(IReadOnlyList<RecentProjectEntry> snapshot, Task previous)
    {
        await Task.Yield();
        await previous;
        await writeGate.WaitAsync();
        Exception? failure = null;
        var temporary = storagePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(storagePath)!);
                var bytes = JsonSerializer.SerializeToUtf8Bytes(snapshot, jsonOptions);
                if (bytes.Length > MAX_FILE_BYTES)
                {
                    throw new InvalidDataException("最近项目记录文件过大。");
                }

                await File.WriteAllBytesAsync(temporary, bytes);
                File.Move(temporary, storagePath, true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException
                                          or NotSupportedException)
        {
            failure = error;
        }
        finally
        {
            writeGate.Release();
        }

        SetError(failure);
    }

    private void SetError(Exception? error)
    {
        if (ReferenceEquals(LastError, error))
        {
            return;
        }

        LastError = error;
        ErrorChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task DisposeCoreAsync(Task pending)
    {
        await pending;
        writeGate.Dispose();
    }
}
