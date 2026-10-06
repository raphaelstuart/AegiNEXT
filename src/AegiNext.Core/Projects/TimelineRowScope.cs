using System.Diagnostics.CodeAnalysis;

namespace AegiNext.Core.Projects;

/// <summary>时间轴属性行所属的稳定实体类型。</summary>
public enum TimelineRowScope
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "工程枚举使用 ALL_UPPER 命名。")]
    SUBTITLE_TRACK,
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "工程枚举使用 ALL_UPPER 命名。")]
    SCENE_LAYER
}
