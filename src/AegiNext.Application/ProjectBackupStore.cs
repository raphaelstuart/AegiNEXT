using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>保存工程原始快照，并仅按当前工程的严格时间戳文件名裁剪备份数量。</summary>
public static class ProjectBackupStore
{
    private const string TIMESTAMP_FORMAT = "yyyyMMdd-HHmmssfff";
    private const int TIMESTAMP_LENGTH = 18;
    private const string PROJECT_EXTENSION = ".aeginext";

    /// <summary>不覆盖地提交原始快照备份；时间碰撞顺延毫秒，写盘成功后才裁剪。</summary>
    public static async Task<string> WriteAsync(ProjectDocument snapshot, string projectPath,
        DateTimeOffset timestamp, int maxCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        cancellationToken.ThrowIfCancellationRequested();
        ProjectValidator.Validate(snapshot);
        var fullPath = GetProjectPath(projectPath);
        var directory = GetBackupDirectory(fullPath);
        Directory.CreateDirectory(directory);
        RejectLinkedDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var local = timestamp.ToLocalTime();
        string backup;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            backup = Path.Combine(directory, stem + "-" + local.ToString(TIMESTAMP_FORMAT, CultureInfo.InvariantCulture) +
                PROJECT_EXTENSION);
            try
            {
                await ProjectStore.CreateAsync(snapshot, backup, cancellationToken).ConfigureAwait(false);
                break;
            }
            catch (IOException) when (File.Exists(backup))
            {
                local = local.AddMilliseconds(1);
            }
        }

        try
        {
            await PruneAsync(fullPath, maxCount, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new ProjectBackupPruneException(backup, error);
        }
        return backup;
    }

    /// <summary>删除当前工程超过数量上限的最旧备份；忽略其他文件与资源目录。</summary>
    public static Task PruneAsync(string projectPath, int maxCount, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCount, 1);
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = GetProjectPath(projectPath);
        var directory = GetBackupDirectory(fullPath);
        if (!Directory.Exists(directory))
        {
            return Task.CompletedTask;
        }

        RejectLinkedDirectory(directory);
        var stem = Path.GetFileNameWithoutExtension(fullPath);
        var files = Directory.EnumerateFiles(directory)
            .Where(path => TryReadTimestamp(Path.GetFileName(path), stem, out _))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .Skip(maxCount)
            .ToArray();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file);
        }
        return Task.CompletedTask;
    }

    /// <summary>识别 backup 目录中采用有效时间戳命名的工程备份，不访问文件内容。</summary>
    public static bool IsBackupPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.Equals(Path.GetFileName(directory), "backup", OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return false;
            }

            var name = Path.GetFileName(fullPath);
            var suffixLength = TIMESTAMP_LENGTH + PROJECT_EXTENSION.Length + 1;
            return name.Length > suffixLength &&
                TryReadTimestamp(name, name[..^suffixLength], out _);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryReadTimestamp(string fileName, string stem, out DateTime timestamp)
    {
        var prefix = stem + "-";
        timestamp = default;
        if (fileName.Length != prefix.Length + TIMESTAMP_LENGTH + PROJECT_EXTENSION.Length ||
            !fileName.StartsWith(prefix, StringComparison.Ordinal) ||
            !fileName.EndsWith(PROJECT_EXTENSION, StringComparison.Ordinal))
        {
            return false;
        }

        return DateTime.TryParseExact(fileName.AsSpan(prefix.Length, TIMESTAMP_LENGTH), TIMESTAMP_FORMAT,
            CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);
    }

    private static string GetProjectPath(string projectPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);
        var fullPath = Path.GetFullPath(projectPath);
        if (string.IsNullOrEmpty(Path.GetFileNameWithoutExtension(fullPath)))
        {
            throw new ArgumentException("项目文件必须具有名称。", nameof(projectPath));
        }
        return fullPath;
    }

    private static string GetBackupDirectory(string projectPath) =>
        Path.Combine(Path.GetDirectoryName(projectPath)!, "backup");

    private static void RejectLinkedDirectory(string directory)
    {
        if (new DirectoryInfo(directory).LinkTarget is not null)
        {
            throw new InvalidDataException("备份目录不能是符号链接。");
        }
    }
}
