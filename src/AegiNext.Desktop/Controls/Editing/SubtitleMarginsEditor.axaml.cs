using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>共享的三轴边距输入与只读布局示意，工程提交由宿主处理。</summary>
public sealed partial class SubtitleMarginsEditor : UserControl
{
    public static readonly StyledProperty<SubtitleMarginsDraft?> DraftProperty =
        AvaloniaProperty.Register<SubtitleMarginsEditor, SubtitleMarginsDraft?>(nameof(Draft));
    public static readonly StyledProperty<int> AlignmentProperty =
        AvaloniaProperty.Register<SubtitleMarginsEditor, int>(nameof(Alignment), 7);
    public static readonly StyledProperty<bool> IsExplicitProperty =
        AvaloniaProperty.Register<SubtitleMarginsEditor, bool>(nameof(IsExplicit));
    public static readonly StyledProperty<bool> ShowDiagramProperty =
        AvaloniaProperty.Register<SubtitleMarginsEditor, bool>(nameof(ShowDiagram), true);
    public static readonly StyledProperty<int> CanvasWidthProperty =
        AvaloniaProperty.Register<SubtitleMarginsEditor, int>(nameof(CanvasWidth), 1920);
    public static readonly StyledProperty<int> CanvasHeightProperty =
        AvaloniaProperty.Register<SubtitleMarginsEditor, int>(nameof(CanvasHeight), 1080);
    private readonly StackPanel draftRoot;
    private readonly NumericDraftInput verticalInput;
    private BindingExpressionBase? verticalTooltipBinding;
    private bool attached;

    /// <summary>仅隔离内部草稿上下文，保留外部 Draft 绑定的宿主上下文。</summary>
    public SubtitleMarginsEditor()
    {
        AvaloniaXamlLoader.Load(this);
        draftRoot = this.FindControl<StackPanel>("DraftRoot")!;
        verticalInput = this.FindControl<NumericDraftInput>("MarginVerticalInput")!;
        draftRoot.DataContext = Draft;
        UpdateHints();
        AddHandler(KeyDownEvent, InputKeyDown, RoutingStrategies.Tunnel);
    }

    public SubtitleMarginsDraft? Draft
    {
        get => GetValue(DraftProperty);
        set => SetValue(DraftProperty, value);
    }
    public int Alignment
    {
        get => GetValue(AlignmentProperty);
        set => SetValue(AlignmentProperty, value);
    }
    public bool IsExplicit
    {
        get => GetValue(IsExplicitProperty);
        set => SetValue(IsExplicitProperty, value);
    }
    public bool ShowDiagram
    {
        get => GetValue(ShowDiagramProperty);
        set => SetValue(ShowDiagramProperty, value);
    }
    public int CanvasWidth
    {
        get => GetValue(CanvasWidthProperty);
        set => SetValue(CanvasWidthProperty, value);
    }
    public int CanvasHeight
    {
        get => GetValue(CanvasHeightProperty);
        set => SetValue(CanvasHeightProperty, value);
    }

    /// <summary>将验证焦点移至本控件命名域中的边距输入。</summary>
    public bool FocusInvalidField(string key)
    {
        return key is "MarginLeftInput" or "MarginRightInput" or "MarginVerticalInput" &&
               this.FindControl<NumericDraftInput>(key)!.FocusInput();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DraftProperty && draftRoot is not null)
        {
            draftRoot.DataContext = Draft;
        }
        if (change.Property == AlignmentProperty || change.Property == IsExplicitProperty)
        {
            UpdateHints();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        attached = true;
        UpdateHints();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        attached = false;
        verticalTooltipBinding?.Dispose();
        verticalTooltipBinding = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void UpdateHints()
    {
        if (verticalInput is null)
        {
            return;
        }
        verticalInput.SetCurrentValue(IsEnabledProperty, !IsExplicit);
        verticalTooltipBinding?.Dispose();
        verticalTooltipBinding = attached
            ? verticalInput.Bind(ToolTip.TipProperty, Localization.Observe(VerticalTooltip).ToBinding())
            : null;
        if (!attached)
        {
            ToolTip.SetTip(verticalInput, VerticalTooltip());
        }
    }

    private string VerticalTooltip()
    {
        var hintKey = IsExplicit ? "Workbench.SubtitleMarginsExplicitHint" :
            Alignment is >= 3 and <= 5 ? "Workbench.SubtitleMarginsMiddleHint" : "Workbench.SubtitleMarginsDiagramHint";
        return Localization.Get("Workbench.MarginVertical") + Environment.NewLine + Localization.Get(hintKey);
    }

    private void InputKeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Handled || args.Key != Key.Escape || Draft is not { } draft || args.Source is not Control source)
        {
            return;
        }
        var input = source.GetSelfAndVisualAncestors().OfType<NumericDraftInput>().FirstOrDefault();
        if (input?.Name is { } name && draft.RestoreField(name))
        {
            args.Handled = true;
        }
    }
}
