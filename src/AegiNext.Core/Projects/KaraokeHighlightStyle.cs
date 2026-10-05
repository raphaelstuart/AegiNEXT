namespace AegiNext.Core.Projects;

/// <summary>逐字高亮使用的预设视觉快照，不改变字幕的字体、字号及自然排版。</summary>
public sealed record KaraokeHighlightStyle
{
    public Guid PresetId { get; init; }
    public string PresetName { get; init; } = string.Empty;
    public SceneColor Fill { get; init; } = SceneColor.White;
    public SceneColor Stroke { get; init; } = SceneColor.Black;
    public double StrokeWidth { get; init; }
    public ScenePoint ShadowOffset { get; init; }
    public double ShadowBlur { get; init; }
    public SceneColor ShadowColor { get; init; } = SceneColor.Transparent;

    /// <summary>比较实际绘制属性，预设名称和来源标识不影响外观兼容性。</summary>
    public bool VisuallyEquals(KaraokeHighlightStyle? other)
    {
        return other is not null && Fill == other.Fill && Stroke == other.Stroke && StrokeWidth == other.StrokeWidth &&
            ShadowOffset == other.ShadowOffset && ShadowBlur == other.ShadowBlur && ShadowColor == other.ShadowColor;
    }

    /// <summary>截取样式预设的填充、描边和阴影，保留线性 HDR 颜色。</summary>
    public static KaraokeHighlightStyle FromStyle(Guid presetId, string presetName, SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new()
        {
            PresetId = presetId,
            PresetName = presetName,
            Fill = style.Fill,
            Stroke = style.Stroke,
            StrokeWidth = style.StrokeWidth,
            ShadowOffset = style.ShadowOffset,
            ShadowBlur = style.ShadowBlur,
            ShadowColor = style.ShadowColor
        };
    }
}
