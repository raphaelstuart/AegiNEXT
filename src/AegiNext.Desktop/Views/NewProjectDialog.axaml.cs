using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Views;

/// <summary>呈现项目名称、父目录及最终创建路径。</summary>
public sealed partial class NewProjectDialog : Window, IWindowTitleBarHost
{
    private readonly WindowTitleBar titleBar;
    private IWindowChrome? chrome;
    private readonly NewProjectDialogViewModel? viewModel;
    private readonly HashSet<Key> pressedKeys = [];
    private bool closed;

    /// <summary>为 XAML 加载器构造展示宿主。</summary>
    public NewProjectDialog() : this(null)
    {
    }

    internal NewProjectDialog(NewProjectDialogViewModel? viewModel)
    {
        this.viewModel = viewModel;
        DataContext = viewModel;
        AvaloniaXamlLoader.Load(this);
        titleBar = this.FindControl<WindowTitleBar>("NewProjectTitleBar")!;
        chrome = WindowChrome.Attach(this, titleBar);
        if (viewModel is not null)
        {
            viewModel.Created += OnCreated;
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
        var input = this.FindControl<TextBox>("ProjectNameInput")!;
        input.Focus();
        input.SelectAll();
    }

    private void OnCreated(object? sender, EventArgs e)
    {
        if (!closed)
        {
            Close(true);
        }
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

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
            Close(false);
        }
        else if (e.Key == Key.Enter && viewModel?.CreateCommand.CanExecute(null) == true)
        {
            pressedKeys.Add(e.Key);
            e.Handled = true;
            viewModel.CreateCommand.Execute(null);
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
            viewModel.Created -= OnCreated;
            viewModel.Dispose();
        }
        chrome?.Dispose();
        chrome = null;
    }
}
