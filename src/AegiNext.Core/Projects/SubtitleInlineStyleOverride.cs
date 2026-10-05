using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>局部文字样式；null 继承基础值，显式 false、零和透明色均为有效覆盖。</summary>
public sealed record SubtitleInlineStyleOverride
{
    public string? FontFamily { get; init; }
    public Guid? FontAssetId { get; init; }
    public bool ClearFontAsset { get; init; }
    public double? FontSize { get; init; }
    public bool? Bold { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public bool? Strikethrough { get; init; }
    public SceneColor? Fill { get; init; }
    public SceneColor? Stroke { get; init; }
    public double? StrokeWidth { get; init; }
    public ScenePoint? ShadowOffset { get; init; }
    public double? ShadowBlur { get; init; }
    public SceneColor? ShadowColor { get; init; }

    [JsonIgnore]
    public bool HasOverrides => FontFamily is not null || FontAssetId.HasValue || ClearFontAsset || FontSize.HasValue ||
        Bold.HasValue || Italic.HasValue || Underline.HasValue || Strikethrough.HasValue || Fill.HasValue ||
        Stroke.HasValue || StrokeWidth.HasValue || ShadowOffset.HasValue || ShadowBlur.HasValue || ShadowColor.HasValue;

    /// <summary>解析局部覆盖；显式字体族切换会清除继承的嵌入字体，除非同时指定字体资源。</summary>
    public SubtitleStyle ApplyTo(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style with
        {
            FontFamily = FontFamily ?? style.FontFamily,
            FontAssetId = FontAssetId ?? (ClearFontAsset || FontFamily is not null ? null : style.FontAssetId),
            FontSize = FontSize ?? style.FontSize,
            Bold = Bold ?? style.Bold,
            Italic = Italic ?? style.Italic,
            Underline = Underline ?? style.Underline,
            Strikethrough = Strikethrough ?? style.Strikethrough,
            Fill = Fill ?? style.Fill,
            Stroke = Stroke ?? style.Stroke,
            StrokeWidth = StrokeWidth ?? style.StrokeWidth,
            ShadowOffset = ShadowOffset ?? style.ShadowOffset,
            ShadowBlur = ShadowBlur ?? style.ShadowBlur,
            ShadowColor = ShadowColor ?? style.ShadowColor
        };
    }

    /// <summary>仅合并新覆盖中明确设置的字段，保留其他局部样式。</summary>
    public SubtitleInlineStyleOverride Merge(SubtitleInlineStyleOverride overlay)
    {
        ArgumentNullException.ThrowIfNull(overlay);
        var changesFont = overlay.FontFamily is not null || overlay.FontAssetId.HasValue || overlay.ClearFontAsset;
        return this with
        {
            FontFamily = overlay.FontFamily ?? FontFamily,
            FontAssetId = changesFont ? overlay.FontAssetId : FontAssetId,
            ClearFontAsset = changesFont ? overlay.ClearFontAsset : ClearFontAsset,
            FontSize = overlay.FontSize ?? FontSize,
            Bold = overlay.Bold ?? Bold,
            Italic = overlay.Italic ?? Italic,
            Underline = overlay.Underline ?? Underline,
            Strikethrough = overlay.Strikethrough ?? Strikethrough,
            Fill = overlay.Fill ?? Fill,
            Stroke = overlay.Stroke ?? Stroke,
            StrokeWidth = overlay.StrokeWidth ?? StrokeWidth,
            ShadowOffset = overlay.ShadowOffset ?? ShadowOffset,
            ShadowBlur = overlay.ShadowBlur ?? ShadowBlur,
            ShadowColor = overlay.ShadowColor ?? ShadowColor
        };
    }

    /// <summary>将预设的文字外观转换为完整局部覆盖，不包含整行位置、对齐或边距。</summary>
    public static SubtitleInlineStyleOverride FromStyle(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        return new()
        {
            FontFamily = style.FontFamily, FontAssetId = style.FontAssetId, ClearFontAsset = !style.FontAssetId.HasValue,
            FontSize = style.FontSize, Bold = style.Bold, Italic = style.Italic, Underline = style.Underline,
            Strikethrough = style.Strikethrough, Fill = style.Fill, Stroke = style.Stroke, StrokeWidth = style.StrokeWidth,
            ShadowOffset = style.ShadowOffset, ShadowBlur = style.ShadowBlur, ShadowColor = style.ShadowColor
        };
    }
}
