using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

/// <summary>保留未解析数值文本的通用输入控件；验证与事务由功能面板承担。</summary>
public sealed class NumericDraftInput : NumericUpDown
{
    public static readonly StyledProperty<string> RawTextProperty =
        AvaloniaProperty.Register<NumericDraftInput, string>(nameof(RawText), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    private bool synchronizing;
    private bool preservingDraft;

    /// <summary>取得或设置包含无效和未完成输入的原始草稿。</summary>
    public string RawText
    {
        get => GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == RawTextProperty && !synchronizing && !preservingDraft)
        {
            synchronizing = true;
            try
            {
                SetCurrentValue(TextProperty, RawText);
            }
            finally
            {
                synchronizing = false;
            }
        }
    }

    /// <inheritdoc />
    protected override void OnTextChanged(string? oldValue, string? newValue)
    {
        if (!synchronizing && !preservingDraft)
        {
            synchronizing = true;
            try
            {
                SetCurrentValue(RawTextProperty, newValue ?? string.Empty);
            }
            finally
            {
                synchronizing = false;
            }
        }
        base.OnTextChanged(oldValue, newValue);
    }

    /// <inheritdoc />
    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        var text = RawText;
        var valid = decimal.TryParse(text, NumberStyles.Float, NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat,
            out var value) && value >= Minimum && value <= Maximum;
        if (valid)
        {
            base.OnLostFocus(e);
            return;
        }
        preservingDraft = true;
        try
        {
            base.OnLostFocus(e);
            SetCurrentValue(TextProperty, text);
        }
        finally
        {
            preservingDraft = false;
        }
    }
}
