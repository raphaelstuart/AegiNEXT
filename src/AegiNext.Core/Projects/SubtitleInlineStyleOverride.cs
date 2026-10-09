using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>局部文字样式；null 继承基础值，显式 false、零和透明色均为有效覆盖。</summary>
public sealed record SubtitleInlineStyleOverride
{
    public string? FontFamily { get; init; }
    public Guid? FontAssetId { get; init; }
    public bool ClearFontAsset { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SubtitleFontVariant? FontVariant { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool ClearFontVariant { get; init; }
    public double? FontSize { get; init; }
    public double? LetterSpacing { get; init; }
    public double? FillBlur { get; init; }
    public double? StrokeBlur { get; init; }
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
    public bool HasOverrides => FontFamily is not null || FontAssetId.HasValue || ClearFontAsset ||
        FontVariant is not null || ClearFontVariant || FontSize.HasValue || LetterSpacing.HasValue || FillBlur.HasValue || StrokeBlur.HasValue ||
        Bold.HasValue || Italic.HasValue || Underline.HasValue || Strikethrough.HasValue || Fill.HasValue ||
        Stroke.HasValue || StrokeWidth.HasValue || ShadowOffset.HasValue || ShadowBlur.HasValue || ShadowColor.HasValue;

    /// <summary>解析局部覆盖；字体切换清除继承身份，命名变体同步其格式状态。</summary>
    public SubtitleStyle ApplyTo(SubtitleStyle style)
    {
        ArgumentNullException.ThrowIfNull(style);
        var changesFont = FontFamily is not null || FontAssetId.HasValue;
        var changesFormatting = Bold.HasValue && Bold != style.Bold || Italic.HasValue && Italic != style.Italic;
        return style with
        {
            FontFamily = FontFamily ?? style.FontFamily,
            FontAssetId = FontAssetId ?? (ClearFontAsset || FontFamily is not null || FontVariant is not null ? null : style.FontAssetId),
            FontVariant = FontVariant ?? (ClearFontVariant || changesFont || changesFormatting ? null : style.FontVariant),
            FontSize = FontSize ?? style.FontSize,
            LetterSpacing = LetterSpacing ?? style.LetterSpacing,
            FillBlur = FillBlur ?? style.FillBlur,
            StrokeBlur = StrokeBlur ?? style.StrokeBlur,
            Bold = Bold ?? (FontVariant is { } variant ? variant.Weight >= 700 : style.Bold),
            Italic = Italic ?? FontVariant?.Italic ?? style.Italic,
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
        var changesFont = overlay.FontFamily is not null || overlay.FontAssetId.HasValue || overlay.ClearFontAsset ||
            overlay.FontVariant is not null;
        var changesVariant = changesFont || overlay.FontVariant is not null || overlay.ClearFontVariant;
        var changesFormatting = FontVariant is { } variant &&
            (overlay.Bold.HasValue && overlay.Bold != (variant.Weight >= 700) ||
             overlay.Italic.HasValue && overlay.Italic != variant.Italic);
        return this with
        {
            FontFamily = overlay.FontFamily ?? FontFamily,
            FontAssetId = changesFont ? overlay.FontAssetId : FontAssetId,
            ClearFontAsset = changesFont ? overlay.ClearFontAsset : ClearFontAsset,
            FontVariant = changesVariant || changesFormatting ? overlay.FontVariant : FontVariant,
            ClearFontVariant = changesVariant ? overlay.ClearFontVariant : changesFormatting || ClearFontVariant,
            FontSize = overlay.FontSize ?? FontSize,
            LetterSpacing = overlay.LetterSpacing ?? LetterSpacing,
            FillBlur = overlay.FillBlur ?? FillBlur,
            StrokeBlur = overlay.StrokeBlur ?? StrokeBlur,
            Bold = overlay.Bold ?? (overlay.FontVariant is { } selected ? selected.Weight >= 700 : Bold),
            Italic = overlay.Italic ?? overlay.FontVariant?.Italic ?? Italic,
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
            FontVariant = style.FontVariant, ClearFontVariant = style.FontVariant is null,
            FontSize = style.FontSize, Bold = style.Bold, Italic = style.Italic, Underline = style.Underline,
            LetterSpacing = style.LetterSpacing, FillBlur = style.FillBlur, StrokeBlur = style.StrokeBlur,
            Strikethrough = style.Strikethrough, Fill = style.Fill, Stroke = style.Stroke, StrokeWidth = style.StrokeWidth,
            ShadowOffset = style.ShadowOffset, ShadowBlur = style.ShadowBlur, ShadowColor = style.ShadowColor
        };
    }
}
