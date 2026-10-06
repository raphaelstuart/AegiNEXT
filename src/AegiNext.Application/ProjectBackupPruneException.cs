namespace AegiNext.Application;

/// <summary>备份已经提交，但后续数量裁剪失败。</summary>
public sealed class ProjectBackupPruneException : IOException
{
    /// <summary>创建携带已提交备份路径的裁剪错误。</summary>
    public ProjectBackupPruneException(string backupPath, Exception innerException)
        : base("项目备份已保存，但旧备份清理失败。", innerException)
    {
        BackupPath = backupPath;
    }

    public string BackupPath { get; }
}
