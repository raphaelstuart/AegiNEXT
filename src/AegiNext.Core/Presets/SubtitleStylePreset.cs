using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Presets;

/// <summary>可跨工程迁移的字幕样式；Style 不持有工程字体标识，导入字体由 Font 自包含携带。</summary>
public sealed record SubtitleStylePreset(Guid Id, string Name, SubtitleStyle Style, EmbeddedSubtitleFont? Font = null,
    TimingPostProcessorOptions? TimingPostProcessor = null);
