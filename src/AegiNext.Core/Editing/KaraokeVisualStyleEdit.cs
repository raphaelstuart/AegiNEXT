using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>瞬态逐字视觉编辑；阴影两个分量独立继承各字的有效值，不改变工程存储结构。</summary>
public sealed record KaraokeVisualStyleEdit
{
    public SceneColor? Fill { get; init; }
    public SceneColor? Stroke { get; init; }
    public double? StrokeWidth { get; init; }
    public double? FillBlur { get; init; }
    public double? StrokeBlur { get; init; }
    public double? ShadowX { get; init; }
    public double? ShadowY { get; init; }
    public double? ShadowBlur { get; init; }
    public SceneColor? ShadowColor { get; init; }
    public bool HasChanges => Fill.HasValue || Stroke.HasValue || StrokeWidth.HasValue || ShadowX.HasValue ||
        ShadowY.HasValue || ShadowBlur.HasValue || ShadowColor.HasValue || FillBlur.HasValue || StrokeBlur.HasValue;

    /// <summary>以当前字的有效阴影为基础，将明确修改的分量转换成可持久化视觉覆盖。</summary>
    public KaraokeVisualStyleOverride ToOverride(SubtitleStyle current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return new()
        {
            Fill = Fill, Stroke = Stroke, StrokeWidth = StrokeWidth, ShadowColor = ShadowColor, ShadowBlur = ShadowBlur,
            FillBlur = FillBlur, StrokeBlur = StrokeBlur,
            ShadowOffset = ShadowX.HasValue || ShadowY.HasValue
                ? new(ShadowX ?? current.ShadowOffset.X, ShadowY ?? current.ShadowOffset.Y) : null
        };
    }

    /// <summary>合并明确修改的字段，保留未修改的阴影分量。</summary>
    public KaraokeVisualStyleEdit Merge(KaraokeVisualStyleEdit overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return this with
        {
            Fill = overlay.Fill ?? Fill, Stroke = overlay.Stroke ?? Stroke, StrokeWidth = overlay.StrokeWidth ?? StrokeWidth,
            FillBlur = overlay.FillBlur ?? FillBlur, StrokeBlur = overlay.StrokeBlur ?? StrokeBlur,
            ShadowX = overlay.ShadowX ?? ShadowX, ShadowY = overlay.ShadowY ?? ShadowY,
            ShadowBlur = overlay.ShadowBlur ?? ShadowBlur, ShadowColor = overlay.ShadowColor ?? ShadowColor
        };
    }

    /// <summary>将样式预设的全部视觉字段转换为一次明确编辑，忽略文字排版字段。</summary>
    public static KaraokeVisualStyleEdit FromStyle(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new()
        {
            Fill = style.Fill, Stroke = style.Stroke, StrokeWidth = style.StrokeWidth, ShadowColor = style.ShadowColor,
            FillBlur = style.FillBlur, StrokeBlur = style.StrokeBlur,
            ShadowX = style.ShadowOffset.X, ShadowY = style.ShadowOffset.Y, ShadowBlur = style.ShadowBlur
        };
    }
}
