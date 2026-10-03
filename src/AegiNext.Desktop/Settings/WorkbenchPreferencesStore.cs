using System.Text.Json;

namespace AegiNext.Desktop.Settings;

/// <summary>原子保存应用偏好，不与工程资产共用目录。</summary>
public sealed class WorkbenchPreferencesStore : IDisposable
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);

    /// <summary>创建存储；测试可注入专用目录。</summary>
    public WorkbenchPreferencesStore(string? directory = null)
    {
        var baseDirectory = directory ??
                            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                                "AegiNext");
        if (string.IsNullOrWhiteSpace(baseDirectory))
        {
            throw new InvalidOperationException("应用数据目录不可用。");
        }

        DirectoryPath = Path.GetFullPath(baseDirectory);
        path = Path.Combine(DirectoryPath, "preferences.json");
    }

    public Exception? LoadError { get; private set; }
    public string DirectoryPath { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        gate.Dispose();
    }

    /// <summary>读取设置；缺失使用默认值，损坏或不可读保留诊断供界面显示。</summary>
    public WorkbenchPreferences Load()
    {
        LoadError = null;
        try
        {
            if (!File.Exists(path))
            {
                return new();
            }

            if (new FileInfo(path).Length > 65536)
            {
                throw new InvalidDataException("偏好文件过大。");
            }

            var value = JsonSerializer.Deserialize<WorkbenchPreferences>(File.ReadAllBytes(path))
                        ?? throw new InvalidDataException("偏好文件为空。");
            value = WorkbenchPreferencesMigration.Upgrade(value);
            value.Validate();
            return value;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException
                                          or JsonException)
        {
            LoadError = error;
            return new();
        }
    }

    /// <summary>串行写入同目录临时文件后替换，取消不提交未完成的设置。</summary>
    public async Task SaveAsync(WorkbenchPreferences value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(temporary, JsonSerializer.SerializeToUtf8Bytes(value), cancellationToken)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            gate.Release();
        }
    }
}
