using System.Reflection;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WindowTitleBarUiTests
{
    [AvaloniaFact]
    public void SharedChromeSynchronizesTheWindowTitleWithoutReplacingContent()
    {
        var (window, titleBar, content) = CreateHost("Initial project");
        using var chrome = WindowChrome.Attach(window, titleBar);
        try
        {
            window.Show();
            FlushLayout(window);
            var title = UiTestActions.Find<TextBlock>(titleBar, "TitleText");
            Assert.Equal("Initial project", title.Text);
            Assert.Same(content, Assert.IsType<Grid>(window.Content).Children[1]);

            window.Title = "Renamed project •";
            FlushLayout(window);

            Assert.Equal("Renamed project •", titleBar.Title);
            Assert.Equal("Renamed project •", title.Text);
            Assert.Same(content, Assert.IsType<Grid>(window.Content).Children[1]);
            Assert.Equal(WindowDecorations.Full, window.WindowDecorations);
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public void NativeCaptionInsetsKeepMenuAndTitleSeparateFromReservedButtonAreas()
    {
        var (window, titleBar, _) = CreateHost("AegiNext fixture");
        var menu = new Button { Content = "Layout", Width = 100 };
        titleBar.MenuContent = menu;
        titleBar.CaptionInsets = new(92, 0, 146, 0);
        using var chrome = WindowChrome.Attach(window, titleBar);
        try
        {
            window.Show();
            FlushLayout(window);

            var menuBounds = WindowBounds(menu, window);
            var titleBounds = WindowBounds(UiTestActions.Find<TextBlock>(titleBar, "TitleText"), window);
            var barBounds = WindowBounds(titleBar, window);
            Assert.True(menuBounds.Width > 0);
            Assert.True(titleBounds.Width > 0);
            Assert.True(menuBounds.Left >= barBounds.Left + titleBar.CaptionInsets.Left);
            Assert.True(menuBounds.Right <= titleBounds.Left);
            Assert.True(titleBounds.Right <= barBounds.Right - titleBar.CaptionInsets.Right);
            Assert.False(titleBar.IsDragRegion(new(barBounds.Left + 20, barBounds.Center.Y)));
            Assert.False(titleBar.IsDragRegion(new(barBounds.Right - 20, barBounds.Center.Y)));
            Assert.True(titleBar.IsDragRegion(titleBounds.Center));
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public void ActualMenuClickRemainsInteractiveWhileBlankTitleSpaceAllowsDragging()
    {
        var (window, titleBar, content) = CreateHost("Interactive fixture");
        var menu = new Button { Content = "Layouts", Width = 110 };
        titleBar.MenuContent = menu;
        var clicks = 0;
        menu.Click += (_, _) => clicks++;
        using var chrome = WindowChrome.Attach(window, titleBar);
        try
        {
            window.Show();
            FlushLayout(window);

            var menuBounds = WindowBounds(menu, window);
            var titleBounds = WindowBounds(UiTestActions.Find<TextBlock>(titleBar, "TitleText"), window);
            var blankTitlePoint = new Point((menuBounds.Right + titleBounds.Left) / 2, titleBounds.Center.Y);
            Assert.False(titleBar.IsDragRegion(menuBounds.Center));
            Assert.True(titleBar.IsDragRegion(blankTitlePoint));
            Assert.False(titleBar.IsDragRegion(WindowBounds(content, window).Center));

            window.MouseDown(menuBounds.Center, MouseButton.Left);
            window.MouseUp(menuBounds.Center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, clicks);
            Assert.True(window.IsVisible);
            Assert.False(titleBar.IsDragRegion(menuBounds.Center));
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public void PlatformChromeHitTestCoversBlankTitleSpaceAndExcludesMenusAndCaptionInsets()
    {
        var (window, titleBar, content) = CreateHost("Platform chrome fixture");
        var menu = new Button { Content = "Layouts", Width = 110 };
        titleBar.MenuContent = menu;
        titleBar.CaptionInsets = new(92, 0, 146, 0);
        using var chrome = WindowChrome.Attach(window, titleBar);
        try
        {
            window.Show();
            FlushLayout(window);
            var barBounds = WindowBounds(titleBar, window);
            var titleBounds = WindowBounds(UiTestActions.Find<TextBlock>(titleBar, "TitleText"), window);
            var menuBounds = WindowBounds(menu, window);
            var points = new[]
            {
                new Point(barBounds.Left + titleBar.CaptionInsets.Left + 2, barBounds.Center.Y),
                new Point((menuBounds.Right + titleBounds.Left) / 2, barBounds.Center.Y),
                new Point(titleBounds.Right + 2, barBounds.Center.Y),
                new Point(titleBounds.Center.X, barBounds.Top + 2),
                new Point(titleBounds.Center.X, barBounds.Bottom - 2),
                titleBounds.Center
            };
            foreach (var point in points)
            {
                Assert.True(titleBar.IsDragRegion(point));
                Assert.Equal(WindowDecorationsElementRole.TitleBar, PlatformChromeRole(window, point));
                Assert.Equal(WindowDecorationsElementRole.TitleBar, MacOsChromeRole(window, point));
            }

            Assert.Equal(WindowDecorationsElementRole.User, PlatformChromeRole(window, menuBounds.Center));
            Assert.Equal(WindowDecorationsElementRole.User,
                PlatformChromeRole(window, new(barBounds.Left + 20, barBounds.Center.Y)));
            Assert.Equal(WindowDecorationsElementRole.User,
                PlatformChromeRole(window, new(barBounds.Right - 20, barBounds.Center.Y)));
            Assert.NotEqual(WindowDecorationsElementRole.TitleBar, MacOsChromeRole(window, menuBounds.Center));
            Assert.NotEqual(WindowDecorationsElementRole.TitleBar,
                MacOsChromeRole(window, new(barBounds.Left + 20, barBounds.Center.Y)));
            Assert.NotEqual(WindowDecorationsElementRole.TitleBar,
                MacOsChromeRole(window, new(barBounds.Right - 20, barBounds.Center.Y)));
            Assert.NotEqual(WindowDecorationsElementRole.TitleBar,
                PlatformChromeRole(window, WindowBounds(content, window).Center));

            titleBar.MenuContent = null;
            titleBar.Title = string.Empty;
            FlushLayout(window);

            Assert.Equal(WindowDecorationsElementRole.TitleBar,
                PlatformChromeRole(window, new(barBounds.Left + 240, barBounds.Center.Y)));
            Assert.Equal(WindowDecorationsElementRole.TitleBar,
                MacOsChromeRole(window, new(barBounds.Left + 240, barBounds.Center.Y)));
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public void ClosingOwnerClosesSharedChromeFloatingAndSettingsHosts()
    {
        var (owner, ownerTitleBar, _) = CreateHost("Main host");
        var (floating, floatingTitleBar, _) = CreateHost("Floating host");
        var (settings, settingsTitleBar, _) = CreateHost("Settings host");
        using var ownerChrome = WindowChrome.Attach(owner, ownerTitleBar);
        using var floatingChrome = WindowChrome.Attach(floating, floatingTitleBar);
        using var settingsChrome = WindowChrome.Attach(settings, settingsTitleBar);
        try
        {
            owner.Show();
            floating.Show(owner);
            settings.Show(owner);
            FlushLayout(owner);
            FlushLayout(floating);
            FlushLayout(settings);

            Assert.Equal("Main host", UiTestActions.Find<TextBlock>(ownerTitleBar, "TitleText").Text);
            Assert.Equal("Floating host", UiTestActions.Find<TextBlock>(floatingTitleBar, "TitleText").Text);
            Assert.Equal("Settings host", UiTestActions.Find<TextBlock>(settingsTitleBar, "TitleText").Text);
            Assert.Contains(floating, owner.OwnedWindows);
            Assert.Contains(settings, owner.OwnedWindows);

            owner.Close();
            Dispatcher.UIThread.RunJobs();

            Assert.False(owner.IsVisible);
            Assert.False(floating.IsVisible);
            Assert.False(settings.IsVisible);
            Assert.Empty(owner.OwnedWindows);
        }
        finally
        {
            settings.Close();
            floating.Close();
            owner.Close();
            Assert.False(settings.IsVisible);
            Assert.False(floating.IsVisible);
            Assert.False(owner.IsVisible);
        }
    }

    [AvaloniaFact]
    public void ResizeClientUpdatesTheClientAndContentButRejectsUseAfterDisposal()
    {
        var (window, titleBar, content) = CreateHost("Resize fixture");
        var chrome = WindowChrome.Attach(window, titleBar);
        try
        {
            window.Show();
            chrome.ResizeClient(new(720, 360));
            FlushLayout(window);

            Assert.Equal(new Size(720, 360), window.ClientSize);
            Assert.Equal(720, titleBar.Bounds.Width);
            Assert.Equal(720, content.Bounds.Width);
            Assert.True(content.Bounds.Height > 0);
            var clientSize = window.ClientSize;
            chrome.Dispose();

            Assert.Throws<ObjectDisposedException>(() => chrome.ResizeClient(new(800, 400)));
            Assert.Equal(clientSize, window.ClientSize);
        }
        finally
        {
            chrome.Dispose();
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData(0, 300)]
    [InlineData(640, 0)]
    [InlineData(-1, 300)]
    [InlineData(640, -1)]
    [InlineData(double.NaN, 300)]
    [InlineData(640, double.NaN)]
    [InlineData(double.PositiveInfinity, 300)]
    [InlineData(640, double.NegativeInfinity)]
    public void InvalidResizePreservesTheExistingClientSize(double width, double height)
    {
        var (window, titleBar, _) = CreateHost("Invalid resize fixture");
        using var chrome = WindowChrome.Attach(window, titleBar);
        try
        {
            window.Show();
            FlushLayout(window);
            var clientSize = window.ClientSize;

            Assert.Throws<ArgumentOutOfRangeException>(() => chrome.ResizeClient(new(width, height)));
            FlushLayout(window);

            Assert.Equal(clientSize, window.ClientSize);
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    private static (Window Window, WindowTitleBar TitleBar, Border Content) CreateHost(string title)
    {
        var titleBar = new WindowTitleBar();
        var content = new Border { Child = new TextBlock { Text = "Window content" } };
        var root = new Grid { RowDefinitions = new("Auto,*") };
        Grid.SetRow(content, 1);
        root.Children.Add(titleBar);
        root.Children.Add(content);
        return (new Window { Title = title, Width = 640, Height = 300, Content = root }, titleBar, content);
    }

    private static Rect WindowBounds(Control control, Window window)
    {
        var position = control.TranslatePoint(default, window)
                       ?? throw new InvalidOperationException("The shared chrome control is not attached to its host.");
        return new(position, control.Bounds.Size);
    }

    private static WindowDecorationsElementRole? PlatformChromeRole(Window window, Point point)
    {
        var inputRoot = typeof(TopLevel).GetProperty("InputRoot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
        var hitTest = typeof(IInputRoot).GetMethod("HitTestChromeElement", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return (WindowDecorationsElementRole?)hitTest.Invoke(inputRoot, [point]);
    }

    private static WindowDecorationsElementRole MacOsChromeRole(Window window, Point point)
    {
        var inputRoot = typeof(TopLevel).GetProperty("InputRoot", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(window)!;
        var sourceType = inputRoot.GetType();
        var renderer = sourceType.GetProperty("Renderer")!.GetValue(inputRoot)!;
        var root = (Visual)sourceType.GetProperty("RootElement")!.GetValue(inputRoot)!;
        var hitTest = renderer.GetType().GetMethod("HitTestFirst")!;
        Func<Visual, bool> filter = value => value is not IInputElement input || input.IsHitTestVisible && input.IsEffectivelyVisible;
        var visual = (Visual?)hitTest.Invoke(renderer, [point, root, filter]);
        Assert.NotNull(visual);
        return WindowDecorationProperties.GetElementRole(visual);
    }

    private static void FlushLayout(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
