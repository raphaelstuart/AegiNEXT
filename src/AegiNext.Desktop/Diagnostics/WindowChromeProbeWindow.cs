using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Platform;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class WindowChromeProbeWindow : Window
{
    private readonly List<IDisposable> localizationBindings = [];
    private readonly IWindowChrome chrome;
    private readonly Menu windowMenu = new();
    private readonly NativeMenu nativeMenu = new();
    private readonly NativeMenuItem nativeWindowGroup;
    private readonly CheckBox rejectClose;
    private readonly TextBlock status;
    private readonly Size initialSize;
    private BindingExpressionBase? statusBinding;

    internal WindowChromeProbeWindow(string host, Size size, Action toggleMenu, Action resize, Action showChildren)
    {
        Host = host;
        initialSize = size;
        localizationBindings.Add(this.Bind(TitleProperty, ObserveTitle(host).ToBinding()));
        Width = size.Width;
        Height = size.Height;
        MinWidth = 480;
        MinHeight = 300;
        TitleBar = new();
        status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        rejectClose = new();
        localizationBindings.Add(rejectClose.Bind(ContentControl.ContentProperty, Localization.Observe("WindowChromeProbe.RejectClose").ToBinding()));
        var root = new DockPanel();
        DockPanel.SetDock(TitleBar, Avalonia.Controls.Dock.Top);
        root.Children.Add(TitleBar);
        var content = new StackPanel { Spacing = 16, Margin = new(24) };
        var description = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        localizationBindings.Add(description.Bind(TextBlock.TextProperty, Localization.Observe("WindowChromeProbe.Description").ToBinding()));
        content.Children.Add(description);
        content.Children.Add(CreateButton("WindowMenu", toggleMenu, localizationBindings));
        content.Children.Add(CreateButton("Resize", resize, localizationBindings));
        if (host == "Main")
        {
            content.Children.Add(CreateButton("Children", showChildren, localizationBindings));
        }

        content.Children.Add(rejectClose);
        content.Children.Add(status);
        root.Children.Add(content);
        Content = root;

        var menuCommand = new WorkbenchCommandAdapter(toggleMenu, () => true);
        var resizeCommand = new WorkbenchCommandAdapter(resize, () => true);
        var childrenCommand = new WorkbenchCommandAdapter(showChildren, () => true);
        var nativeBranch = new NativeMenu();
        nativeBranch.Items.Add(CreateNativeItem("WindowMenu", menuCommand, localizationBindings));
        nativeBranch.Items.Add(CreateNativeItem("Resize", resizeCommand, localizationBindings));
        nativeBranch.Items.Add(CreateNativeItem("Children", childrenCommand, localizationBindings));
        nativeWindowGroup = new() { Menu = nativeBranch };
        localizationBindings.Add(nativeWindowGroup.Bind(NativeMenuItem.HeaderProperty, Localization.Observe("WindowChromeProbe.Window").ToBinding()));
        NativeMenu.SetMenu(this, nativeMenu);
        var windowBranch = new MenuItem();
        localizationBindings.Add(windowBranch.Bind(MenuItem.HeaderProperty, Localization.Observe("WindowChromeProbe.Window").ToBinding()));
        windowBranch.Items.Add(CreateMenuItem("WindowMenu", menuCommand, localizationBindings));
        windowBranch.Items.Add(CreateMenuItem("Resize", resizeCommand, localizationBindings));
        windowBranch.Items.Add(CreateMenuItem("Children", childrenCommand, localizationBindings));
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
            statusBinding?.Dispose();
            statusBinding = status.Bind(TextBlock.TextProperty, Localization.Observe("WindowChromeProbe.CloseCancelled").ToBinding());
        }

        base.OnClosing(e);
    }

    private static IObservable<string> ObserveTitle(string host)
    {
        return Localization.Observe(() =>
            $"{Localization.Get("WindowChromeProbe." + host)} — AegiNext · {Localization.Get("WindowChromeProbe.Probe")}");
    }

    private static Button CreateButton(string key, Action execute, List<IDisposable> bindings)
    {
        var button = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Left,
            Command = new WorkbenchCommandAdapter(execute, () => true)
        };
        bindings.Add(button.Bind(ContentControl.ContentProperty, Localization.Observe("WindowChromeProbe." + key).ToBinding()));
        return button;
    }

    private static NativeMenuItem CreateNativeItem(string key, WorkbenchCommandAdapter command, List<IDisposable> bindings)
    {
        var item = new NativeMenuItem { Command = command };
        bindings.Add(item.Bind(NativeMenuItem.HeaderProperty, Localization.Observe("WindowChromeProbe." + key).ToBinding()));
        return item;
    }

    private static MenuItem CreateMenuItem(string key, WorkbenchCommandAdapter command, List<IDisposable> bindings)
    {
        var item = new MenuItem { Command = command };
        bindings.Add(item.Bind(MenuItem.HeaderProperty, Localization.Observe("WindowChromeProbe." + key).ToBinding()));
        return item;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        ResizeClient(initialSize);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Opened -= OnOpened;
        Closed -= OnClosed;
        statusBinding?.Dispose();
        statusBinding = null;
        foreach (var binding in localizationBindings)
        {
            binding.Dispose();
        }

        localizationBindings.Clear();
        chrome.Dispose();
    }
}
