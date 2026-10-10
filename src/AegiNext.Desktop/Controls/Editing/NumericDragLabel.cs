using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace AegiNext.Desktop.Controls;

/// <summary>通过左右拖动标题编辑数值草稿，释放后发送一次语义提交通知。</summary>
public sealed class NumericDragLabel : TextBlock
{
    public static readonly StyledProperty<NumericDraftInput?> InputProperty =
        AvaloniaProperty.Register<NumericDragLabel, NumericDraftInput?>(nameof(Input));
    public static readonly RoutedEvent<NumericDragEventArgs> DragStartedEvent =
        RoutedEvent.Register<NumericDragLabel, NumericDragEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<NumericDragEventArgs> DragCompletedEvent =
        RoutedEvent.Register<NumericDragLabel, NumericDragEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble);
    public static readonly RoutedEvent<NumericDragEventArgs> DragCanceledEvent =
        RoutedEvent.Register<NumericDragLabel, NumericDragEventArgs>(nameof(DragCanceled), RoutingStrategies.Bubble);
    private NumericDraftInput? activeInput;
    private IPointer? pointer;
    private Visual? gestureRoot;
    private string originalText = string.Empty;
    private decimal? originalValue;
    private double originalNumber;
    private double startX;
    private double increment;
    private decimal minimum;
    private decimal maximum;
    private bool ending;

    /// <summary>创建使用水平调整光标的可聚焦标题。</summary>
    public NumericDragLabel()
    {
        Focusable = true;
        IsTabStop = false;
        Cursor = new(StandardCursorType.SizeWestEast);
        Background = Brushes.Transparent;
        AddHandler(PointerPressedEvent, OnTitlePointerPressed, RoutingStrategies.Tunnel);
    }

    /// <summary>取得或设置该标题编辑的数值输入。</summary>
    public NumericDraftInput? Input
    {
        get => GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    /// <summary>开始后通知消费者冻结业务目标并取消延迟失焦提交。</summary>
    public event EventHandler<NumericDragEventArgs>? DragStarted
    {
        add => AddHandler(DragStartedEvent, value);
        remove => RemoveHandler(DragStartedEvent, value);
    }

    /// <summary>释放后通知消费者验证目标并提交一次。</summary>
    public event EventHandler<NumericDragEventArgs>? DragCompleted
    {
        add => AddHandler(DragCompletedEvent, value);
        remove => RemoveHandler(DragCompletedEvent, value);
    }

    /// <summary>取消后通知消费者刷新已恢复的草稿预览。</summary>
    public event EventHandler<NumericDragEventArgs>? DragCanceled
    {
        add => AddHandler(DragCanceledEvent, value);
        remove => RemoveHandler(DragCanceledEvent, value);
    }

    /// <summary>取消未提交手势，恢复开始时的原文和数值投影。</summary>
    public void CancelDrag() => EndDrag(false);

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(TextBlock);

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (activeInput is not null && pointer == e.Pointer)
        {
            e.Handled = true;
        }
    }

    private void OnTitlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var input = Input;
        var format = input?.NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || input is null ||
            !input.IsEffectivelyEnabled || activeInput is not null ||
            !double.TryParse(input.RawText, NumberStyles.Float, format, out var number) || !double.IsFinite(number) ||
            number < (double)input.Minimum || number > (double)input.Maximum)
        {
            return;
        }
        activeInput = input;
        input.PropertyChanged += OnInputPropertyChanged;
        input.DetachedFromVisualTree += OnInputDetached;
        gestureRoot = TopLevel.GetTopLevel(this);
        pointer = e.Pointer;
        originalText = input.RawText;
        originalValue = input.Value;
        originalNumber = number;
        startX = e.GetPosition(gestureRoot).X;
        increment = input.Increment > 0 ? (double)input.Increment : 1;
        minimum = input.Minimum;
        maximum = input.Maximum;
        input.BeginTitleDrag();
        RaiseEvent(new NumericDragEventArgs(DragStartedEvent, input, originalText, originalText));
        if (activeInput is null)
        {
            return;
        }
        Focus();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (activeInput is not { } input || e.Pointer != pointer)
        {
            return;
        }
        var delta = e.GetPosition(gestureRoot).X - startX;
        var number = Math.Clamp(originalNumber + delta * increment, (double)minimum, (double)maximum);
        var format = input.NumberFormat ?? CultureInfo.CurrentCulture.NumberFormat;
        string text;
        if (decimal.TryParse(originalText, NumberStyles.Float, format, out var decimalNumber) &&
            (!input.PreserveDoublePrecision || (double)decimalNumber == originalNumber) &&
            number >= (double)decimal.MinValue && number <= (double)decimal.MaxValue)
        {
            decimal projected;
            try
            {
                projected = Math.Clamp(decimalNumber + (decimal)(delta * increment), minimum, maximum);
            }
            catch (OverflowException)
            {
                projected = delta > 0 ? maximum : minimum;
            }
            text = projected.ToString("G29", format);
        }
        else
        {
            text = number.ToString("G17", format);
        }
        input.SetCurrentValue(NumericDraftInput.RawTextProperty, delta == 0 ? originalText : text);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (activeInput is not null && e.Pointer == pointer)
        {
            EndDrag(true);
            e.Handled = true;
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && activeInput is not null)
        {
            CancelDrag();
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        if (!ending)
        {
            CancelDrag();
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelDrag();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == InputProperty || change.Property == DataContextProperty ||
            change.Property == IsVisibleProperty && !IsVisible || change.Property == IsEnabledProperty && !IsEnabled)
        {
            CancelDrag();
        }
    }

    private void EndDrag(bool commit)
    {
        if (activeInput is not { } input || ending)
        {
            return;
        }
        ending = true;
        try
        {
            input.PropertyChanged -= OnInputPropertyChanged;
            input.DetachedFromVisualTree -= OnInputDetached;
            if (!commit)
            {
                input.SetCurrentValue(NumericUpDown.ValueProperty, originalValue);
                input.SetCurrentValue(NumericDraftInput.RawTextProperty, originalText);
            }
            activeInput = null;
            pointer?.Capture(null);
            pointer = null;
            gestureRoot = null;
            input.EndTitleDrag();
            RaiseEvent(new NumericDragEventArgs(commit ? DragCompletedEvent : DragCanceledEvent, input, originalText, input.RawText));
        }
        finally
        {
            ending = false;
        }
    }

    private void OnInputPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == DataContextProperty || e.Property == IsEnabledProperty ||
            e.Property == NumericUpDown.MinimumProperty || e.Property == NumericUpDown.MaximumProperty)
        {
            CancelDrag();
        }
    }

    private void OnInputDetached(object? sender, VisualTreeAttachmentEventArgs e) => CancelDrag();
}
