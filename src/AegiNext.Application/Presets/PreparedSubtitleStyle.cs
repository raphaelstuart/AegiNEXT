using AegiNext.Core.Projects;

namespace AegiNext.Application.Presets;

/// <summary>已解析字体资源的工程快照、样式及其身份，可用于字幕、轨道默认样式及高亮样式事务。</summary>
public sealed record PreparedSubtitleStyle(ProjectDocument Project, SubtitleStyle Style, string StyleName = "Default",
    Guid? StylePresetId = null);
