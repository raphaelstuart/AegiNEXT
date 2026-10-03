using System.Text.Json;
using System.Globalization;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkspaceLayoutStore
{
    private const long MAX_FILE_LENGTH = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    private readonly string path;
    private readonly object writeLock = new();
    private Task pendingWrite = Task.CompletedTask;

    internal WorkspaceLayoutStore(string personalDirectory)
    {
        path = Path.Combine(personalDirectory, "layouts.json");
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
            if (new FileInfo(path).Length > MAX_FILE_LENGTH)
            {
                throw new InvalidDataException("The layout file exceeds the size limit.");
            }
            var result = JsonSerializer.Deserialize<WorkspaceLayoutFile>(File.ReadAllText(path), jsonOptions)
                ?? throw new InvalidDataException("The layout file is empty.");
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

    internal Task SaveAsync(WorkspaceLayoutFile file)
    {
        ValidateFile(file);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(file, jsonOptions);
        lock (writeLock)
        {
            pendingWrite = WriteAfterAsync(pendingWrite, bytes);
            return pendingWrite;
        }
    }

    internal Task FlushAsync()
    {
        lock (writeLock)
        {
            return pendingWrite;
        }
    }

    internal static string Fingerprint(WorkspaceLayoutSnapshot layout)
    {
        return JsonSerializer.Serialize(layout);
    }

    private async Task WriteAfterAsync(Task previous, byte[] bytes)
    {
        try
        {
            await previous.ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }

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
