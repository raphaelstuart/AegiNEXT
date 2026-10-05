using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;
using Avalonia.Utilities;

namespace AegiNext.Desktop.Controls;

/// <summary>在原生文本布局上提供逐词语法颜色；绘制、输入法、选择和命中共享同一布局。</summary>
public sealed class EffectScriptTextPresenter : TextPresenter
{
    private Size constraint;
    private static readonly IBrush?[] lightPalette = [null, new SolidColorBrush(Color.Parse("#7C2FA2")),
        new SolidColorBrush(Color.Parse("#185ABD")), new SolidColorBrush(Color.Parse("#A6431C")),
        new SolidColorBrush(Color.Parse("#39721D")), new SolidColorBrush(Color.Parse("#667487")),
        new SolidColorBrush(Color.Parse("#006D83")), new SolidColorBrush(Color.Parse("#8C5A00"))];
    private static readonly IBrush?[] darkPalette = [null, new SolidColorBrush(Color.Parse("#C792EA")),
        new SolidColorBrush(Color.Parse("#82AAFF")), new SolidColorBrush(Color.Parse("#F78C6C")),
        new SolidColorBrush(Color.Parse("#C3E88D")), new SolidColorBrush(Color.Parse("#8392A8")),
        new SolidColorBrush(Color.Parse("#89DDFF")), new SolidColorBrush(Color.Parse("#FFCB6B"))];

    /// <summary>主题变化时重新创建语法颜色，不改变编辑器源或选择。</summary>
    public EffectScriptTextPresenter()
    {
        ActualThemeVariantChanged += (_, _) => InvalidateTextLayout();
    }

    /// <summary>按关键字、属性、数值、字符串和注释分别生成语法样式。</summary>
    protected override TextLayout CreateTextLayout()
    {
        var source = Text ?? string.Empty;
        var preedit = PreeditText;
        if (!string.IsNullOrEmpty(preedit))
        {
            source = source.Insert(Math.Clamp(CaretIndex, 0, source.Length), preedit);
        }

        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        var styles = new List<ValueSpan<TextRunProperties>>();
        var selectionStart = Math.Min(SelectionStart, SelectionEnd);
        var selectionEnd = Math.Max(SelectionStart, SelectionEnd);
        var preeditStart = Math.Clamp(CaretIndex, 0, source.Length);
        var preeditEnd = preeditStart + (preedit?.Length ?? 0);
        foreach (var token in EffectScriptLanguage.Tokenize(source))
        {
            var start = token.Start;
            var end = start + token.Length;
            var boundaries = new[] { start, Math.Clamp(selectionStart, start, end), Math.Clamp(selectionEnd, start, end),
                Math.Clamp(preeditStart, start, end), Math.Clamp(preeditEnd, start, end), end }.Distinct().Order().ToArray();
            for (var index = 0; index < boundaries.Length - 1; index++)
            {
                var spanStart = boundaries[index];
                var selected = ShowSelectionHighlight && spanStart >= selectionStart && spanStart < selectionEnd;
                var brush = selected && SelectionForegroundBrush is not null ? SelectionForegroundBrush : TokenBrush(token.Kind);
                var inPreedit = spanStart >= preeditStart && spanStart < preeditEnd;
                styles.Add(new(spanStart, boundaries[index + 1] - spanStart,
                    new GenericTextRunProperties(typeface, FontSize, inPreedit ? TextDecorations.Underline : null,
                        foregroundBrush: brush, fontFeatures: FontFeatures)));
            }
        }

        return new(source, typeface, FontSize, Foreground, TextAlignment, TextWrapping, flowDirection: FlowDirection,
            maxWidth: constraint.Width > 0 ? constraint.Width : double.PositiveInfinity,
            maxHeight: constraint.Height > 0 ? constraint.Height : double.PositiveInfinity,
            lineHeight: LineHeight, letterSpacing: LetterSpacing, fontFeatures: FontFeatures, textStyleOverrides: styles);
    }

    /// <summary>保留原生文本呈现器测量并记录本轮布局约束。</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        constraint = availableSize;
        return base.MeasureOverride(availableSize);
    }

    /// <summary>布局变化时保持颜色文本与光标几何一致。</summary>
    protected override Size ArrangeOverride(Size finalSize)
    {
        constraint = finalSize;
        return base.ArrangeOverride(finalSize);
    }

    private IBrush? TokenBrush(EffectScriptTokenKind kind)
    {
        var palette = ActualThemeVariant == ThemeVariant.Dark ? darkPalette : lightPalette;
        return palette[(int)kind] ?? Foreground;
    }
}
