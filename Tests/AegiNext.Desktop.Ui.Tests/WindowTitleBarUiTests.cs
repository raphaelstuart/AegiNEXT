using System.Reflection;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Tasks;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Chrome;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WindowTitleBarUiTests
{
    [AvaloniaFact]
    public async Task MainWindowTaskButtonUsesTheRightmostSlotInsideCaptionInsets()
    {
        await using var context = new MainWindowTestContext();
        var window = context.Window;
        window.Width = 760;
        var titleBar = UiTestActions.Find<WindowTitleBar>(window, "TitleBar");
        titleBar.CaptionInsets = new(72, 0, 100, 0);
        FlushLayout(window);
        var view = Assert.Single(titleBar.GetVisualDescendants().OfType<TaskCenterView>());
        var button = UiTestActions.Find<Button>(view, "TasksButton");
        var bounds = WindowBounds(button, window);
        var barBounds = WindowBounds(titleBar, window);
        Assert.InRange(Math.Abs(bounds.Right - (barBounds.Right - titleBar.CaptionInsets.Right - 8)), 0, 1);
        Assert.Null(titleBar.CenterContent);
        Assert.Same(view, titleBar.RightContent);
        Assert.False(titleBar.IsDragRegion(bounds.Center));
        Assert.True(button.Focus());
        UiTestActions.Press(window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.True(button.Flyout!.IsOpen);
        UiTestActions.Press(window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.False(button.Flyout.IsOpen);
        Assert.True(button.IsFocused);
    }

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

    [AvaloniaTheory]
    [InlineData(420, 64)]
    [InlineData(760, 112)]
    public void CenterContentUsesActualWidthAndRemainsInteractiveOutsideTheDragRegion(double width, double buttonWidth)
    {
        var (window, titleBar, _) = CreateHost("A long project title that should be trimmed to the available area");
        window.Width = width;
        titleBar.CaptionInsets = new(72, 0, 100, 0);
        var menu = new Button { Content = "Menu", Width = 48 };
        var center = new Button { Content = "Tasks", Width = buttonWidth };
        titleBar.MenuContent = menu;
        titleBar.CenterContent = center;
        var clicks = 0;
        center.Click += (_, _) => clicks++;
        try
        {
            window.Show();
            FlushLayout(window);
            var centerBounds = WindowBounds(center, window);
            var menuBounds = WindowBounds(menu, window);
            var titleBounds = WindowBounds(UiTestActions.Find<TextBlock>(titleBar, "TitleText"), window);
            var contentCenter = (titleBar.CaptionInsets.Left + width - titleBar.CaptionInsets.Right) / 2;
            Assert.InRange(Math.Abs(centerBounds.Center.X - contentCenter), 0, 1);
            Assert.True(menuBounds.Right <= centerBounds.Left);
            Assert.True(centerBounds.Right <= titleBounds.Left);
            Assert.True(titleBounds.Right <= width - titleBar.CaptionInsets.Right);
            Assert.False(titleBar.IsDragRegion(centerBounds.Center));
            Assert.False(titleBar.IsDragRegion(new(centerBounds.Center.X, titleBar.Bounds.Top + 2)));
            Assert.Equal(WindowDecorationsElementRole.User, PlatformChromeRole(window, centerBounds.Center));
            Assert.NotEqual(WindowDecorationsElementRole.TitleBar, MacOsChromeRole(window, centerBounds.Center));
            window.MouseDown(centerBounds.Center, MouseButton.Left);
            window.MouseUp(centerBounds.Center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, clicks);
            var originalAvailable = titleBar.AvailableMenuWidth;
            center.Width += 20;
            FlushLayout(window);
            Assert.InRange(originalAvailable - titleBar.AvailableMenuWidth, 9, 11);
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData(420, 64, 72, 0)]
    [InlineData(760, 112, 0, 138)]
    [InlineData(760, 112, 92, 146)]
    public void RightContentReservesItsMeasuredWidthAndStaysBeforeNativeButtons(double width, double buttonWidth,
        double leftInset, double rightInset)
    {
        var (window, titleBar, _) = CreateHost("A long project title that should be trimmed to the available area");
        window.Width = width;
        titleBar.CaptionInsets = new(leftInset, 0, rightInset, 0);
        var menu = new Button { Content = "Menu", Width = 48 };
        var right = new Button { Content = "Tasks", Width = buttonWidth };
        titleBar.MenuContent = menu;
        titleBar.RightContent = right;
        var rightPresenter = UiTestActions.Find<ContentControl>(titleBar, "RightPresenter");
        var clicks = 0;
        right.Click += (_, _) => clicks++;
        try
        {
            window.Show();
            FlushLayout(window);
            var rightBounds = WindowBounds(right, window);
            var menuBounds = WindowBounds(menu, window);
            var titleBounds = WindowBounds(UiTestActions.Find<TextBlock>(titleBar, "TitleText"), window);
            Assert.Equal(width - rightInset - 8, rightBounds.Right, precision: 4);
            Assert.True(menuBounds.Right <= titleBounds.Left);
            Assert.True(titleBounds.Right <= rightBounds.Left);
            Assert.False(titleBar.IsDragRegion(rightBounds.Center));
            Assert.False(titleBar.IsDragRegion(new(rightBounds.Center.X, titleBar.Bounds.Top + 2)));
            if (rightInset > 0)
            {
                Assert.False(titleBar.IsDragRegion(new(width - 2, rightBounds.Center.Y)));
            }
            Assert.True(titleBar.IsDragRegion(titleBounds.Center));
            Assert.Equal(WindowDecorationsElementRole.User, PlatformChromeRole(window, rightBounds.Center));
            Assert.NotEqual(WindowDecorationsElementRole.TitleBar, MacOsChromeRole(window, rightBounds.Center));
            window.MouseDown(rightBounds.Center, MouseButton.Left);
            window.MouseUp(rightBounds.Center, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, clicks);
            var originalAvailable = titleBar.AvailableMenuWidth;
            right.Width += 20;
            FlushLayout(window);
            Assert.True(titleBar.AvailableMenuWidth < originalAvailable);
            Assert.Equal(width - rightInset - 8, WindowBounds(rightPresenter, window).Right, precision: 4);
            Assert.Equal(right.Width, rightPresenter.Bounds.Width, precision: 4);
            Assert.True(WindowBounds(UiTestActions.Find<TextBlock>(titleBar, "TitleText"), window).Right <=
                WindowBounds(rightPresenter, window).Left);
            titleBar.CaptionInsets = new(leftInset, 0, rightInset + 25, 0);
            FlushLayout(window);
            Assert.Equal(width - rightInset - 25 - 8, WindowBounds(rightPresenter, window).Right, precision: 4);
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData(96U)]
    [InlineData(144U)]
    [InlineData(192U)]
    public void WindowsCaptionMeasurementKeepsRightContentOutsideTheNativeAperture(uint dpi)
    {
        var (window, titleBar, _) = CreateHost("Windows caption fixture");
        var clientWidth = WindowsChromeGeometry.ToPixels(window.Width, dpi);
        var buttonWidth = WindowsChromeGeometry.ToPixels(138, dpi);
        var nativeWindow = new WindowsRect { Left = -1920, Top = -100, Right = -1920 + clientWidth, Bottom = 980 };
        var origin = new WindowsPoint { X = -1920, Y = -100 };
        var buttons = new WindowsRect { Left = clientWidth - buttonWidth, Right = clientWidth, Bottom = 45 };
        titleBar.CaptionInsets = WindowsChromeGeometry.GetCaptionInsets(nativeWindow, origin, clientWidth, buttons, dpi);
        var right = new Button { Content = "Tasks", Width = 64 };
        titleBar.RightContent = right;
        try
        {
            window.Show();
            FlushLayout(window);
            var aperture = WindowsChromeGeometry.GetCaptionAperture(nativeWindow, origin, window.ClientSize, buttons, dpi);
            var rightBounds = WindowBounds(right, window);
            Assert.True(rightBounds.Right < aperture.Left);
            Assert.False(aperture.Intersects(rightBounds));
            Assert.False(titleBar.IsDragRegion(aperture.Center));
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
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
