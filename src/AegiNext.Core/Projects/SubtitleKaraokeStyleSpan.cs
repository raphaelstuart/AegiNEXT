namespace AegiNext.Core.Projects;

/// <summary>完整字素文字范围的高亮视觉覆盖，与计时组及启用状态独立。</summary>
public sealed record SubtitleKaraokeStyleSpan(int Utf16Start, int Utf16Length,
    KaraokeVisualStyleOverride? ActiveStyle = null, KaraokeVisualStyleOverride? InactiveStyle = null);
