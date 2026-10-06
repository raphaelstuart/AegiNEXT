using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Application;

/// <summary>项目编辑器通知的状态变化来源。</summary>
public enum ProjectEditorChangeKind
{
    DOCUMENT,
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目状态枚举按已锁定的 ALL_UPPER 公共契约命名。")]
    SAVE_POINT
}
