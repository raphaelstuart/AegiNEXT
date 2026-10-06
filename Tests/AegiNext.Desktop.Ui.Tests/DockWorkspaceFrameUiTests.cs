using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class DockWorkspaceFrameUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DockedAndFloatingWorkspacesUseRoundedThemeFramesWithoutActiveBlueHeaders(bool dark)
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        main.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Flush(main);
        var panel = main.Panels[WorkbenchPanelIds.PREVIEW];
        var docked = panel.GetVisualAncestors().OfType<WorkbenchToolChromeControl>().Single();
        AssertFrame(docked, dark);

        main.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(main.Layouts.FloatingWindows);
        try
        {
            floating.Width = 360;
            floating.Height = 300;
            floating.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
            Flush(floating);
            Assert.Same(panel, main.Panels[WorkbenchPanelIds.PREVIEW]);
            Assert.Same(floating, TopLevel.GetTopLevel(panel));
            var chrome = panel.GetVisualAncestors().OfType<WorkbenchToolChromeControl>().Single();
            var frame = AssertFrame(chrome, dark);
            var panelFrame = Assert.IsType<Border>(((UserControl)panel).Content);
            Assert.Equal(frame.CornerRadius, panelFrame.CornerRadius);
            AssertRoundedPixels(frame, dark);
            Capture(floating, dark ? "dock-rounded-floating-dark.png" : "dock-rounded-floating-light.png");
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static Border AssertFrame(WorkbenchToolChromeControl chrome, bool dark)
    {
        var frame = Assert.IsType<Border>(chrome.GetVisualParent());
        Assert.Equal("PART_WorkspaceFrame", frame.Name);
        Assert.Equal(new CornerRadius(10), frame.CornerRadius);
        Assert.Equal(new Thickness(1), frame.BorderThickness);
        Assert.True(frame.ClipToBounds);
        Assert.Equal(Color.Parse(dark ? "#303949" : "#DDE2EA"), Assert.IsAssignableFrom<ISolidColorBrush>(frame.BorderBrush).Color);
        var header = chrome.GetVisualDescendants().OfType<Grid>().Single(value => value.Name == "PART_Grip");
        for (var state = 0; state < 2; state++)
        {
            chrome.SetCurrentValue(Dock.Avalonia.Controls.ToolChromeControl.IsActiveProperty, state == 1);
            Assert.Equal(Color.Parse(dark ? "#1C222D" : "#FFFFFF"), Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color);
        }
        var content = chrome.GetVisualDescendants().OfType<WorkbenchToolControl>().Single();
        var contentFrame = content.GetVisualDescendants().OfType<Border>()
            .Single(value => value.Name == "PART_Border" && ReferenceEquals(value.TemplatedParent, content));
        Assert.Equal(default, contentFrame.BorderThickness);
        return frame;
    }

    private static void AssertRoundedPixels(Border frame, bool dark)
    {
        using var target = new RenderTargetBitmap(new((int)Math.Ceiling(frame.Bounds.Width),
            (int)Math.Ceiling(frame.Bounds.Height)), new(96, 96));
        target.Render(frame);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        Assert.Equal(0, pixels.GetPixel(1, 1).Alpha);
        var surface = dark ? new SKColor(28, 34, 45) : SKColors.White;
        Assert.Equal(surface, pixels.GetPixel(pixels.Width / 2, 8));
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
