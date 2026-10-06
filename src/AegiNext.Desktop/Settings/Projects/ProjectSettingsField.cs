using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Desktop.Settings.Projects;

/// <summary>项目设置中可独立确认或恢复的输入字段。</summary>
[SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "字段标识沿用项目枚举的全大写命名约定。")]
public enum ProjectSettingsField
{
    WORKSPACE_ROOT,
    AUTO_SAVE_INTERVAL,
    BACKUP_INTERVAL,
    MAXIMUM_BACKUP_COUNT
}
