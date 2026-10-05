using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>可复用 DSL 编辑器，只处理文本输入、语法颜色、上下文补全与诊断定位。</summary>
public sealed partial class EffectScriptEditor : UserControl
{
    public static readonly StyledProperty<string> SourceProperty = AvaloniaProperty.Register<EffectScriptEditor, string>(
        nameof(Source), string.Empty, defaultBindingMode: BindingMode.TwoWay);
    public static readonly StyledProperty<bool> IsReadOnlyProperty = AvaloniaProperty.Register<EffectScriptEditor, bool>(nameof(IsReadOnly));
    public static readonly StyledProperty<int> DiagnosticLineProperty = AvaloniaProperty.Register<EffectScriptEditor, int>(nameof(DiagnosticLine));
    public static readonly StyledProperty<int> DiagnosticColumnProperty = AvaloniaProperty.Register<EffectScriptEditor, int>(nameof(DiagnosticColumn), 1);
    private readonly TextBox input;
    private readonly ListBox completions;
    private readonly Popup completionPopup;
    private TextPresenter? presenter;
    private bool consumingSpace;
    private int completionRevision;

    /// <summary>装配本地原生文本框及补全导航，禁止访问工程、Dock 或主窗口。</summary>
    public EffectScriptEditor()
    {
        AvaloniaXamlLoader.Load(this);
        input = this.FindControl<TextBox>("ScriptTextInput")!;
        completions = this.FindControl<ListBox>("ScriptCompletions")!;
        completionPopup = this.FindControl<Popup>("ScriptCompletionPopup")!;
        completionPopup.PlacementTarget = input;
        AddHandler(KeyDownEvent, EditorKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, EditorKeyUp, RoutingStrategies.Tunnel);
        input.AddHandler(TextInputEvent, (_, _) => ScheduleCompletions(), RoutingStrategies.Bubble, true);
        input.TemplateApplied += (_, args) =>
        {
            if (presenter is not null)
            {
                presenter.PropertyChanged -= PresenterPropertyChanged;
            }

            presenter = args.NameScope.Find<TextPresenter>("PART_TextPresenter");
            if (presenter is not null)
            {
                presenter.PropertyChanged += PresenterPropertyChanged;
            }
        };
        input.TextChanged += (_, _) =>
        {
            if (completionPopup.IsOpen)
            {
                ScheduleCompletions();
            }
        };
        input.PropertyChanged += (_, args) =>
        {
            if (completionPopup.IsOpen && args.Property == TextBox.CaretIndexProperty)
            {
                ScheduleCompletions();
            }
        };
        completions.AddHandler(PointerReleasedEvent, (_, args) =>
        {
            if (args.InitialPressMouseButton == MouseButton.Left)
            {
                AcceptCompletion();
            }
        }, RoutingStrategies.Bubble, true);
        input.LostFocus += (_, _) => HideCompletions();
        completionPopup.Closed += (_, _) => completions.IsVisible = false;
        RefreshLanguage();
    }

    public string Source
    {
        get => GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool IsReadOnly
    {
        get => GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    public int DiagnosticLine
    {
        get => GetValue(DiagnosticLineProperty);
        set => SetValue(DiagnosticLineProperty, value);
    }

    public int DiagnosticColumn
    {
        get => GetValue(DiagnosticColumnProperty);
        set => SetValue(DiagnosticColumnProperty, value);
    }

    /// <summary>刷新补全和诊断文字，不改变源代码、插入点和未提交草稿。</summary>
    public void RefreshLanguage()
    {
        if (completionPopup.IsOpen)
        {
            ShowCompletions();
        }
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Localization.LanguageChanged += OnLanguageChanged;
        RefreshLanguage();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshLanguage();

    /// <summary>显式跳转到解析器报告的行列，不在失焦或自动验证时抢焦点。</summary>
    public void RevealDiagnostic()
    {
        if (DiagnosticLine <= 0)
        {
            return;
        }

        var offset = 0;
        var lines = Source.Split('\n');
        for (var line = 0; line < Math.Min(DiagnosticLine - 1, lines.Length - 1); line++)
        {
            offset += lines[line].Length + 1;
        }

        var lineLength = lines[Math.Clamp(DiagnosticLine - 1, 0, lines.Length - 1)].TrimEnd('\r').Length;
        offset += Math.Clamp(DiagnosticColumn - 1, 0, lineLength);
        input.Focus();
        input.CaretIndex = Math.Clamp(offset, 0, Source.Length);
        input.SelectionStart = input.CaretIndex;
        input.SelectionEnd = Math.Min(input.CaretIndex + 1, Source.Length);
    }

    /// <summary>诊断更新只显示定位入口；不触发隐式输入提交。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DiagnosticLineProperty && input is not null)
        {
            this.FindControl<Button>("ScriptLocateError")!.IsVisible = DiagnosticLine > 0;
        }
        else if (change.Property == IsReadOnlyProperty && IsReadOnly && completionPopup is not null)
        {
            HideCompletions();
        }
    }

    /// <summary>离开宿主时关闭补全浮层，并取消尚未执行的输入请求。</summary>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        Localization.LanguageChanged -= OnLanguageChanged;
        HideCompletions();
        consumingSpace = false;
        base.OnDetachedFromVisualTree(e);
    }

    private void EditorKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            e.Handled = true;
            consumingSpace = true;
            if (!IsReadOnly)
            {
                ShowCompletions();
            }

            return;
        }

        if (!completionPopup.IsOpen || !string.IsNullOrEmpty(presenter?.PreeditText))
        {
            return;
        }

        if (e.Key is Key.Enter or Key.Tab)
        {
            e.Handled = true;
            AcceptCompletion();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            HideCompletions();
        }
        else if (e.Key is Key.Up or Key.Down)
        {
            e.Handled = true;
            var count = completions.ItemCount;
            completions.SelectedIndex = Math.Clamp(completions.SelectedIndex + (e.Key == Key.Up ? -1 : 1), 0, count - 1);
        }
    }

    private void EditorKeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && consumingSpace)
        {
            consumingSpace = false;
            e.Handled = true;
        }
    }

    private void ShowCompletions()
    {
        if (IsReadOnly || !input.IsFocused || !string.IsNullOrEmpty(presenter?.PreeditText) || input.SelectionStart != input.SelectionEnd)
        {
            HideCompletions();
            return;
        }

        var items = EffectScriptLanguage.Complete(input.Text ?? string.Empty, input.CaretIndex);
        var selectedInsertion = (completions.SelectedItem as EffectScriptCompletion)?.Insertion;
        completions.ItemsSource = items;
        var selectedIndex = items.Count > 0 ? 0 : -1;
        for (var index = 0; index < items.Count; index++)
        {
            if (items[index].Insertion == selectedInsertion)
            {
                selectedIndex = index;
                break;
            }
        }

        completions.SelectedIndex = selectedIndex;
        completions.IsVisible = items.Count > 0;
        if (items.Count == 0)
        {
            HideCompletions();
            return;
        }

        if (presenter is not null)
        {
            var caret = presenter.TextLayout.HitTestTextPosition(input.CaretIndex);
            var point = presenter.TranslatePoint(caret.TopLeft, input) ?? new Point(input.Padding.Left, input.Padding.Top);
            var x = Math.Clamp(point.X, 0, input.Bounds.Width);
            var y = Math.Clamp(point.Y, 0, Math.Max(0, input.Bounds.Height - caret.Height));
            completionPopup.PlacementRect = new Rect(x, y, Math.Max(1, caret.Width), caret.Height);
        }

        completionPopup.IsOpen = true;
    }

    private void ScheduleCompletions()
    {
        var revision = ++completionRevision;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision == completionRevision && this.IsAttachedToVisualTree())
            {
                ShowCompletions();
            }
        }, DispatcherPriority.Input);
    }

    private void HideCompletions()
    {
        completionRevision++;
        completionPopup.IsOpen = false;
        completions.IsVisible = false;
    }

    private void PresenterPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == TextPresenter.PreeditTextProperty && !string.IsNullOrEmpty(presenter?.PreeditText))
        {
            HideCompletions();
        }
    }

    private void AcceptCompletion()
    {
        if (completions.SelectedItem is not EffectScriptCompletion item || IsReadOnly)
        {
            return;
        }

        var source = input.Text ?? string.Empty;
        if (item.Start + item.Length > source.Length)
        {
            HideCompletions();
            return;
        }

        HideCompletions();
        input.SelectionStart = item.Start;
        input.SelectionEnd = item.Start + item.Length;
        input.SelectedText = item.Insertion;
        input.CaretIndex = item.Start + item.Insertion.Length;
        input.SelectionStart = input.CaretIndex;
        input.SelectionEnd = input.CaretIndex;
    }

    private void LocateError(object? sender, RoutedEventArgs e)
    {
        RevealDiagnostic();
    }
}
