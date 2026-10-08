using System.Text.Json;
using AegiNext.Application.Tasks;
using System.Globalization;
using AegiNext.Desktop.Settings.Transfer;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkspaceLayoutStore : IDisposable
{
    internal const int MAXIMUM_FILE_BYTES = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string path;
    private readonly object writeLock = new();
    private readonly HashSet<Task> pendingWrites = [];
    private readonly AegiTaskService? tasks;
    private readonly SemaphoreSlim atomicWriteGate = new(1, 1);

    internal WorkspaceLayoutStore(string personalDirectory, AegiTaskService? tasks = null)
    {
        path = Path.Combine(personalDirectory, "layouts.json");
        this.tasks = tasks;
    }

    public string? DiagnosticPath { get; private set; }
    public string? LoadError { get; private set; }

    internal WorkspaceLayoutFile Load()
    {
        if (!File.Exists(path))
        {
            return new();
        }

        try
        {
            if (new FileInfo(path).Length > MAXIMUM_FILE_BYTES)
            {
                throw new InvalidDataException("The layout file exceeds the size limit.");
            }
            var result = JsonSerializer.Deserialize<WorkspaceLayoutFile>(File.ReadAllText(path), jsonOptions)
                ?? throw new InvalidDataException("The layout file is empty.");
            result = WorkspaceLayoutMigration.Upgrade(result);
            ValidateFile(result);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException or NullReferenceException)
        {
            LoadError = exception.Message;
            DiagnosticPath = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
            try
            {
                File.Copy(path, DiagnosticPath, false);
                File.WriteAllText(DiagnosticPath + ".txt", exception.ToString());
            }
            catch (Exception diagnosticException) when (diagnosticException is IOException or UnauthorizedAccessException)
            {
                DiagnosticPath = path;
            }
            return new();
        }
    }

    internal WorkspaceLayoutFile LoadStrict()
    {
        if (!File.Exists(path))
        {
            return new();
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length > MAXIMUM_FILE_BYTES)
        {
            throw new InvalidDataException("The layout file exceeds the size limit.");
        }

        var bytes = new byte[checked((int)stream.Length)];
        stream.ReadExactly(bytes);
        return Deserialize(bytes);
    }

    internal static byte[] Serialize(WorkspaceLayoutFile file)
    {
        ValidateFile(file);
        return SettingsTransferJson.Serialize(file, MAXIMUM_FILE_BYTES);
    }

    internal static WorkspaceLayoutFile Deserialize(ReadOnlySpan<byte> bytes)
    {
        try
        {
            var file = SettingsTransferJson.Deserialize<WorkspaceLayoutFile>(bytes, MAXIMUM_FILE_BYTES);
            file = WorkspaceLayoutMigration.Upgrade(file);
            ValidateFile(file);
            return file;
        }
        catch (Exception error) when (error is ArgumentException or NullReferenceException)
        {
            throw new InvalidDataException("The layout document is invalid.", error);
        }
    }

    internal AegiTaskResource Resource => AegiTaskResource.DeferredStoragePath(path);

    internal Task SaveAsync(WorkspaceLayoutFile file)
    {
        var bytes = Serialize(file);
        var pending = tasks is null ? WriteAsync(bytes) :
            tasks.Submit(new LayoutWriteTask(this, bytes)).Completion;
        lock (writeLock)
        {
            pendingWrites.Add(pending);
        }
        _ = RemoveCompletedAsync(pending);
        return pending;
    }

    private async Task RemoveCompletedAsync(Task pending)
    {
        try
        {
            await pending.ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
        finally
        {
            lock (writeLock)
            {
                pendingWrites.Remove(pending);
            }
        }
    }

    internal async Task FlushAsync()
    {
        while (true)
        {
            Task[] pending;
            lock (writeLock)
            {
                pending = pendingWrites.ToArray();
            }
            if (pending.Length == 0)
            {
                return;
            }
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
    }

    internal static string Fingerprint(WorkspaceLayoutSnapshot layout)
    {
        return JsonSerializer.Serialize(layout);
    }

    internal async Task WriteAsync(byte[] bytes)
    {
        await atomicWriteGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await WriteCoreAsync(bytes).ConfigureAwait(false);
        }
        finally
        {
            atomicWriteGate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        atomicWriteGate.Dispose();
    }

    private async Task WriteCoreAsync(byte[] bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, ".layouts-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes).ConfigureAwait(false);
                await stream.FlushAsync().ConfigureAwait(false);
                stream.Flush(true);
            }
            File.Move(temporaryPath, path, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void ValidateFile(WorkspaceLayoutFile file)
    {
        if (file is null || file.Current is null || file.Presets is null || file.Presets.Any(preset => preset is null))
        {
            throw new InvalidDataException("The layout document is incomplete.");
        }

        if (file.Version != WorkspaceLayoutSnapshot.CURRENT_VERSION)
        {
            throw new InvalidDataException($"Unsupported layout file version: {file.Version}.");
        }

        WorkspaceLayoutValidator.Validate(file.Current);
        var ids = new HashSet<string>(WorkspaceLayoutPresets.BuiltIn.Select(preset => preset.Id), StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in file.Presets)
        {
            if (preset.IsReadOnly || string.IsNullOrWhiteSpace(preset.Id) || !ids.Add(preset.Id)
                || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 80 || !names.Add(preset.Name))
            {
                throw new InvalidDataException("Invalid personal preset.");
            }
            WorkspaceLayoutValidator.Validate(preset.Layout);
        }

        if (!ids.Contains(file.CurrentPresetId))
        {
            throw new InvalidDataException("The selected preset is missing.");
        }
    }
}
