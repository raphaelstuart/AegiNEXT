using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Views;

/// <summary>呈现带范围校验的通用整数草稿，确认后返回数值，关闭时返回空值。</summary>
public sealed partial class IntegerInputDialog : Window, IWindowTitleBarHost
{
    private readonly WindowTitleBar titleBar;
    private readonly IntegerInputDialogViewModel? viewModel;
    private readonly HashSet<Key> pressedKeys = [];
    private IWindowChrome? chrome;
    private bool closed;

    /// <summary>为 XAML 加载器构造展示宿主。</summary>
    public IntegerInputDialog() : this(null)
    {
    }

    internal IntegerInputDialog(IntegerInputDialogViewModel? viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        titleBar = this.FindControl<WindowTitleBar>("IntegerInputTitleBar")!;
        chrome = WindowChrome.Attach(this, titleBar);
        if (viewModel is not null)
        {
            viewModel.Confirmed += OnConfirmed;
        }

        Opened += OnOpened;
        Closed += OnClosed;
        Deactivated += OnDeactivated;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, true);
    }

    WindowTitleBar IWindowTitleBarHost.TitleBar => titleBar;

    void IWindowTitleBarHost.ReleaseStandaloneChrome()
    {
        chrome?.Dispose();
        chrome = null;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        var input = this.FindControl<NumericDraftInput>("IntegerInput")!;
        var text = input.GetVisualDescendants().OfType<TextBox>().First();
        text.Focus();
        text.SelectAll();
    }

    private void OnConfirmed(object? sender, EventArgs e)
    {
        if (!closed)
        {
            Close(viewModel!.Result);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(null);

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (pressedKeys.Contains(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers != KeyModifiers.None || e.Handled || HasActiveComposition())
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            pressedKeys.Add(e.Key);
            e.Handled = true;
            Close(null);
        }
        else if (e.Key == Key.Enter && viewModel?.ConfirmCommand.CanExecute(null) == true)
        {
            pressedKeys.Add(e.Key);
            e.Handled = true;
            viewModel.ConfirmCommand.Execute(null);
        }
    }

    private bool HasActiveComposition()
    {
        return FocusManager.GetFocusedElement() is Avalonia.Visual focused &&
            focused.GetSelfAndVisualAncestors().OfType<TextBox>().FirstOrDefault() is { } input &&
            input.GetVisualDescendants().OfType<TextPresenter>()
                .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (pressedKeys.Remove(e.Key))
        {
            e.Handled = true;
        }
    }

    private void OnDeactivated(object? sender, EventArgs e) => pressedKeys.Clear();

    private void OnClosed(object? sender, EventArgs e)
    {
        closed = true;
        Opened -= OnOpened;
        Closed -= OnClosed;
        Deactivated -= OnDeactivated;
        RemoveHandler(KeyDownEvent, OnKeyDown);
        RemoveHandler(KeyUpEvent, OnKeyUp);
        pressedKeys.Clear();
        if (viewModel is not null)
        {
            viewModel.Confirmed -= OnConfirmed;
            viewModel.Dispose();
        }

        chrome?.Dispose();
        chrome = null;
    }
}
