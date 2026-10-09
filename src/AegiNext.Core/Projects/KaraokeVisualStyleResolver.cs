namespace AegiNext.Core.Projects;

/// <summary>统一解析普通文字、整句高亮及逐字视觉覆盖，不改变文字排版。</summary>
public static class KaraokeVisualStyleResolver
{
    /// <summary>解析未激活外观，轮廓逐字模式在激活前隐藏描边。</summary>
    public static SubtitleStyle ResolveInactive(SubtitleStyle ordinary, KaraokeSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ordinary);
        ArgumentNullException.ThrowIfNull(segment);
        var inactive = segment.InactiveStyle?.ApplyTo(ordinary) ?? ordinary;
        return segment.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP
            ? inactive with { StrokeWidth = 0 }
            : inactive;
    }

    /// <summary>先继承整句高亮或旧片段填充，再合并逐字覆盖，保留普通文字的字体与字形。</summary>
    public static SubtitleStyle ResolveActive(SubtitleStyle ordinary, KaraokeHighlightStyle? highlight,
        KaraokeSegment segment)
    {
        ArgumentNullException.ThrowIfNull(ordinary);
        ArgumentNullException.ThrowIfNull(segment);
        var active = highlight is not null
            ? ordinary with
            {
                Fill = highlight.Fill, Stroke = highlight.Stroke, StrokeWidth = highlight.StrokeWidth,
                FillBlur = highlight.FillBlur, StrokeBlur = highlight.StrokeBlur,
                ShadowColor = highlight.ShadowColor, ShadowOffset = highlight.ShadowOffset, ShadowBlur = highlight.ShadowBlur
            }
            : ordinary with { Fill = segment.HighlightColor };
        return segment.ActiveStyle?.ApplyTo(active) ?? active;
    }
}
