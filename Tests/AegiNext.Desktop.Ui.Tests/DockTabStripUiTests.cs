using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class DockTabStripUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DockTabsUseOnlyTextAndBackgroundSelectionWithoutBorderSegments(bool dark, bool floating)
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        var theme = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        main.RequestedThemeVariant = theme;
        Flush(main);
        Window host = main;
        if (floating)
        {
            var panel = main.Layouts.PanelAdapters[WorkbenchPanelIds.STYLES];
            main.Layouts.Root.Factory!.FloatAllDockables(panel);
            Dispatcher.UIThread.RunJobs();
            host = Assert.Single(main.Layouts.FloatingWindows);
            host.Width = 240;
            host.Height = 320;
            host.RequestedThemeVariant = theme;
            Flush(host);
        }

        try
        {
            var strip = host.GetVisualDescendants().OfType<ToolTabStrip>()
                .Single(value => value.Items.Cast<WorkbenchDockPanel>().Any(panel => panel.Id == WorkbenchPanelIds.STYLES));
            Assert.Equal(3, strip.ItemCount);
            Assert.Equal(default, strip.BorderThickness);
            AssertStripBorders(strip);
            AssertTabs(strip);

            var effectTab = strip.GetVisualDescendants().OfType<ToolTabStripItem>()
                .Single(value => value.DataContext is WorkbenchDockPanel { Id: WorkbenchPanelIds.EFFECTS });
            var position = effectTab.TranslatePoint(new Point(effectTab.Bounds.Width / 2, effectTab.Bounds.Height / 2), host)!.Value;
            host.MouseDown(position, MouseButton.Left);
            host.MouseUp(position, MouseButton.Left);
            Flush(host);
            Assert.True(effectTab.IsSelected);
            AssertTabs(strip);
            AssertStripBorders(strip);
            Capture(host, $"dock-tabs-{(floating ? "floating" : "docked")}-{(dark ? "dark" : "light")}.png");
        }
        finally
        {
            if (floating)
            {
                host.Close();
                Dispatcher.UIThread.RunJobs();
            }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SinglePanelTabStripDoesNotLeaveABottomSeparator(bool dark)
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        main.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Flush(main);
        var preview = main.Panels[WorkbenchPanelIds.PREVIEW];
        var tool = preview.GetVisualAncestors().OfType<WorkbenchToolControl>().Single();
        var strip = tool.GetVisualDescendants().OfType<ToolTabStrip>().Single();
        Assert.Equal(1, strip.ItemCount);
        AssertStripBorders(strip);
        Assert.Equal(0, strip.Bounds.Height);
        Assert.Equal(default, Assert.IsType<Border>(((UserControl)preview).Content).BorderThickness);
    }

    private static void AssertStripBorders(ToolTabStrip strip)
    {
        var borders = strip.GetVisualDescendants().OfType<Border>()
            .Where(value => ReferenceEquals(value.TemplatedParent, strip))
            .ToArray();
        foreach (var name in new[] { "PART_Border", "PART_BorderLeftFill", "PART_BorderRightFill", "PART_BorderFill" })
        {
            var border = Assert.Single(borders, value => value.Name == name);
            Assert.Equal(default, border.BorderThickness);
        }
    }

    private static void AssertTabs(ToolTabStrip strip)
    {
        var tabs = strip.GetVisualDescendants().OfType<ToolTabStripItem>().ToArray();
        var selected = Assert.Single(tabs, value => value.IsSelected);
        Assert.All(tabs, value =>
        {
            Assert.Equal(default, value.BorderThickness);
            Assert.Equal(new Thickness(8, 4), value.Padding);
            var border = Assert.Single(value.GetVisualDescendants().OfType<Border>(),
                candidate => ReferenceEquals(candidate.TemplatedParent, value));
            Assert.Equal(default, border.BorderThickness);
        });
        var foreground = Assert.IsAssignableFrom<ISolidColorBrush>(selected.Foreground);
        var background = Assert.IsAssignableFrom<ISolidColorBrush>(selected.Background);
        Assert.Equal(foreground.Color, background.Color);
        Assert.Equal(0.14, background.Opacity);
        AssertBorderlessPixels(selected);
    }

    private static void AssertBorderlessPixels(ToolTabStripItem tab)
    {
        using var target = new RenderTargetBitmap(new((int)Math.Ceiling(tab.Bounds.Width),
            (int)Math.Ceiling(tab.Bounds.Height)), new(96, 96));
        target.Render(tab);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        var background = pixels.GetPixel(pixels.Width / 2, 0);
        Assert.InRange(background.Alpha, (byte)1, (byte)254);
        Assert.Equal(background, pixels.GetPixel(0, 0));
        Assert.Equal(background, pixels.GetPixel(pixels.Width - 1, 0));
        Assert.Equal(background, pixels.GetPixel(pixels.Width / 2, pixels.Height - 1));
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var image = window.CaptureRenderedFrame();
        Assert.NotNull(image);
        image.Save(System.IO.Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
