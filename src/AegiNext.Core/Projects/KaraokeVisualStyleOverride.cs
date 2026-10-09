using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>卡拉 OK 演唱前或高亮后的视觉覆盖，不改变字体、字号和排版几何。</summary>
public sealed record KaraokeVisualStyleOverride
{
    public SceneColor? Fill { get; init; }
    public SceneColor? Stroke { get; init; }
    public double? StrokeWidth { get; init; }
    public double? FillBlur { get; init; }
    public double? StrokeBlur { get; init; }
    public ScenePoint? ShadowOffset { get; init; }
    public double? ShadowBlur { get; init; }
    public SceneColor? ShadowColor { get; init; }

    [JsonIgnore]
    public bool HasOverrides => Fill.HasValue || Stroke.HasValue || StrokeWidth.HasValue || ShadowOffset.HasValue ||
        ShadowBlur.HasValue || ShadowColor.HasValue || FillBlur.HasValue || StrokeBlur.HasValue;

    /// <summary>只合并明确设置的字段，显式零和透明色均保留为覆盖。</summary>
    public KaraokeVisualStyleOverride Merge(KaraokeVisualStyleOverride overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        return this with
        {
            Fill = overlay.Fill ?? Fill, Stroke = overlay.Stroke ?? Stroke, StrokeWidth = overlay.StrokeWidth ?? StrokeWidth,
            FillBlur = overlay.FillBlur ?? FillBlur, StrokeBlur = overlay.StrokeBlur ?? StrokeBlur,
            ShadowOffset = overlay.ShadowOffset ?? ShadowOffset, ShadowBlur = overlay.ShadowBlur ?? ShadowBlur,
            ShadowColor = overlay.ShadowColor ?? ShadowColor
        };
    }

    /// <summary>覆盖绘制属性，保留所有文字排版属性。</summary>
    public SubtitleStyle ApplyTo(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style with
        {
            Fill = Fill ?? style.Fill,
            Stroke = Stroke ?? style.Stroke,
            StrokeWidth = StrokeWidth ?? style.StrokeWidth,
            FillBlur = FillBlur ?? style.FillBlur,
            StrokeBlur = StrokeBlur ?? style.StrokeBlur,
            ShadowOffset = ShadowOffset ?? style.ShadowOffset,
            ShadowBlur = ShadowBlur ?? style.ShadowBlur,
            ShadowColor = ShadowColor ?? style.ShadowColor
        };
    }
}
