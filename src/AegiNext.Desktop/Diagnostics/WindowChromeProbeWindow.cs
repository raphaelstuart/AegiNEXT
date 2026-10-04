using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class WindowChromeProbeWindow : Window
{
    private readonly IWindowChrome chrome;
    private readonly Menu windowMenu = new();
    private readonly NativeMenu nativeMenu = new();
    private readonly NativeMenuItem nativeWindowGroup;
    private readonly CheckBox rejectClose;
    private readonly TextBlock status;
    private readonly Size initialSize;

    internal WindowChromeProbeWindow(string host, Size size, Action toggleMenu, Action resize, Action showChildren)
    {
        Host = host;
        initialSize = size;
        Title = $"{WindowChromeProbeText.Get(host)} — AegiNext · {WindowChromeProbeText.Get("Probe")}";
        Width = size.Width;
        Height = size.Height;
        MinWidth = 480;
        MinHeight = 300;
        TitleBar = new();
        status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        rejectClose = new() { Content = WindowChromeProbeText.Get("RejectClose") };
        var root = new DockPanel();
        DockPanel.SetDock(TitleBar, Avalonia.Controls.Dock.Top);
        root.Children.Add(TitleBar);
        var content = new StackPanel { Spacing = 16, Margin = new(24) };
        content.Children.Add(new TextBlock
        {
            Text = WindowChromeProbeText.Get("Description"), TextWrapping = Avalonia.Media.TextWrapping.Wrap
        });
        content.Children.Add(CreateButton("WindowMenu", toggleMenu));
        content.Children.Add(CreateButton("Resize", resize));
        if (host == "Main")
        {
            content.Children.Add(CreateButton("Children", showChildren));
        }

        content.Children.Add(rejectClose);
        content.Children.Add(status);
        root.Children.Add(content);
        Content = root;

        var menuCommand = new WorkbenchCommandAdapter(toggleMenu, () => true);
        var resizeCommand = new WorkbenchCommandAdapter(resize, () => true);
        var childrenCommand = new WorkbenchCommandAdapter(showChildren, () => true);
        var nativeBranch = new NativeMenu();
        nativeBranch.Items.Add(new NativeMenuItem(WindowChromeProbeText.Get("WindowMenu")) { Command = menuCommand });
        nativeBranch.Items.Add(new NativeMenuItem(WindowChromeProbeText.Get("Resize")) { Command = resizeCommand });
        nativeBranch.Items.Add(new NativeMenuItem(WindowChromeProbeText.Get("Children")) { Command = childrenCommand });
        nativeWindowGroup = new(WindowChromeProbeText.Get("Window")) { Menu = nativeBranch };
        NativeMenu.SetMenu(this, nativeMenu);
        var windowBranch = new MenuItem { Header = WindowChromeProbeText.Get("Window") };
        windowBranch.Items.Add(new MenuItem { Header = WindowChromeProbeText.Get("WindowMenu"), Command = menuCommand });
        windowBranch.Items.Add(new MenuItem { Header = WindowChromeProbeText.Get("Resize"), Command = resizeCommand });
        windowBranch.Items.Add(new MenuItem { Header = WindowChromeProbeText.Get("Children"), Command = childrenCommand });
        windowMenu.Items.Add(windowBranch);
        chrome = WindowChrome.Attach(this, TitleBar);
        SetWindowMenu(!OperatingSystem.IsMacOS());
        Opened += OnOpened;
        Closed += OnClosed;
    }

    internal string Host { get; }
    internal WindowTitleBar TitleBar { get; }
    internal bool WindowMenuVisible => TitleBar.MenuContent is not null;
    internal bool RejectNextClose
    {
        get => rejectClose.IsChecked == true;
        set => rejectClose.IsChecked = value;
    }

    internal void SetWindowMenu(bool visible)
    {
        TitleBar.MenuContent = visible && Host == "Main" ? windowMenu : null;
        if (OperatingSystem.IsMacOS() && !visible)
        {
            if (nativeMenu.Items.Count == 0)
            {
                nativeMenu.Items.Add(nativeWindowGroup);
            }
        }
        else
        {
            nativeMenu.Items.Clear();
        }
    }

    internal void ResizeClient(Size size)
    {
        chrome.ResizeClient(size);
    }

    internal WindowChromeProbeSample Capture(string action)
    {
        int? count = null;
        bool? measured = null;
        bool? renderingEnabled = null;
        string? transparency = null;
        double? apertureWidth = null;
        double? apertureHeight = null;
        string? failure = null;
        if (OperatingSystem.IsMacOS() && TryGetPlatformHandle() is IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 } handle)
        {
            count = MacOsCaptionButtons.CountVisible(handle.Handle);
            measured = count == 3 && TitleBar.CaptionInsets.Left > 0;
        }
        else if (OperatingSystem.IsWindows() && chrome is WindowsWindowChrome native)
        {
            measured = native.CaptionButtonsMeasured;
            renderingEnabled = native.NonClientRenderingEnabled;
            transparency = ActualTransparencyLevel.ToString();
            apertureWidth = native.CaptionAperture.Width;
            apertureHeight = native.CaptionAperture.Height;
            failure = native.LastError?.ToString();
        }

        return new(action, Host, Title ?? string.Empty, ClientSize.Width, ClientSize.Height, RenderScaling,
            TitleBar.CaptionInsets.Left, TitleBar.CaptionInsets.Right, ExtendClientAreaToDecorationsHint,
            WindowDecorations.ToString(), WindowState.ToString(), WindowMenuVisible, count, measured, failure,
            renderingEnabled, transparency, apertureWidth, apertureHeight);
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (RejectNextClose)
        {
            RejectNextClose = false;
            e.Cancel = true;
            status.Text = WindowChromeProbeText.Get("CloseCancelled");
        }

        base.OnClosing(e);
    }

    private static Button CreateButton(string key, Action execute)
    {
        return new()
        {
            Content = WindowChromeProbeText.Get(key),
            HorizontalAlignment = HorizontalAlignment.Left,
            Command = new WorkbenchCommandAdapter(execute, () => true)
        };
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        ResizeClient(initialSize);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        chrome.Dispose();
        Opened -= OnOpened;
        Closed -= OnClosed;
    }
}
