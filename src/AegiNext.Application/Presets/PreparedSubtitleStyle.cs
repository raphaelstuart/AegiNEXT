using AegiNext.Core.Projects;

namespace AegiNext.Application.Presets;

/// <summary>已解析字体资源的工程快照与样式，可用于字幕、轨道默认样式及高亮样式事务。</summary>
public sealed record PreparedSubtitleStyle(ProjectDocument Project, SubtitleStyle Style);
