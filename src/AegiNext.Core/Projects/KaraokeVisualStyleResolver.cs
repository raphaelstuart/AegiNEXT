using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>统一解析普通文字、整句高亮及逐字视觉覆盖，不改变文字排版。</summary>
public static class KaraokeVisualStyleResolver
{
    /// <summary>首次启用及未计时文字预览所使用的默认高亮填充。</summary>
    public static SceneColor DefaultHighlightColor { get; } = new(1, 0.6, 0);

    /// <summary>解析未激活外观，轮廓逐字模式在激活前隐藏描边。</summary>
    public static SubtitleStyle ResolveInactive(SubtitleStyle ordinary, KaraokeSegment? segment,
        KaraokeVisualStyleOverride? rangeStyle = null)
    {
        ArgumentNullException.ThrowIfNull(ordinary);
        var inactive = rangeStyle?.ApplyTo(ordinary) ?? ordinary;
        return segment?.HighlightKind == KaraokeHighlightKind.OUTLINE_STEP
            ? inactive with { StrokeWidth = 0 }
            : inactive;
    }

    /// <summary>先继承整句高亮或旧片段填充，再合并逐字覆盖，保留普通文字的字体与字形。</summary>
    public static SubtitleStyle ResolveActive(SubtitleStyle ordinary, KaraokeHighlightStyle? highlight,
        KaraokeSegment? segment, KaraokeVisualStyleOverride? rangeStyle = null)
    {
        ArgumentNullException.ThrowIfNull(ordinary);
        var active = highlight is not null
            ? ordinary with
            {
                Fill = highlight.Fill, Stroke = highlight.Stroke, StrokeWidth = highlight.StrokeWidth,
                FillBlur = highlight.FillBlur, StrokeBlur = highlight.StrokeBlur,
                ShadowColor = highlight.ShadowColor, ShadowOffset = highlight.ShadowOffset, ShadowBlur = highlight.ShadowBlur
            }
            : ordinary with { Fill = segment?.HighlightColor ?? DefaultHighlightColor };
        return rangeStyle?.ApplyTo(active) ?? active;
    }

    /// <summary>在有序文字范围中查询指定字素的状态覆盖，无覆盖时继承默认。</summary>
    public static KaraokeVisualStyleOverride? RangeStyleAt(SubtitleLine line, int utf16Offset,
        KaraokeVisualState state)
    {
        ArgumentNullException.ThrowIfNull(line);
        return StyleAt(line.KaraokeStyleSpans, utf16Offset, state);
    }

    /// <summary>在有序文字范围中查询指定字素的状态覆盖，无覆盖时继承默认。</summary>
    public static KaraokeVisualStyleOverride? StyleAt(ImmutableArray<SubtitleKaraokeStyleSpan> spans,
        int utf16Offset, KaraokeVisualState state)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state));
        }
        var low = 0;
        var high = spans.Length - 1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var span = spans[middle];
            if (utf16Offset < span.Utf16Start)
            {
                high = middle - 1;
            }
            else if (utf16Offset >= span.Utf16Start + span.Utf16Length)
            {
                low = middle + 1;
            }
            else
            {
                return state == KaraokeVisualState.ACTIVE ? span.ActiveStyle : span.InactiveStyle;
            }
        }
        return null;
    }
}
