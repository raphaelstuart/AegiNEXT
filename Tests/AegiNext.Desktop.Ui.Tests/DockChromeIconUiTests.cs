using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;
using Path = Avalonia.Controls.Shapes.Path;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class DockChromeIconUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompactDockButtonsHaveVisibleVectorPixelsInBothThemes(bool dark)
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        main.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        main.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();
        var chrome = main.GetVisualDescendants().OfType<WorkbenchToolChromeControl>().First();
        var button = chrome.GetVisualDescendants().OfType<Button>().Single(value => value.Name == "PART_MenuButton");
        var path = button.GetVisualDescendants().OfType<Path>().Single(value => value.Name == "PART_MenuPath");
        Assert.Contains("dock-chrome", button.Classes);
        Assert.Equal(new Thickness(2), button.Padding);
        Assert.Equal(22, button.MinHeight);
        Assert.NotNull(path.Data);
        Assert.NotNull(path.Fill);
        Assert.True(path.Bounds.Width >= 6 && path.Bounds.Height >= 3, $"Icon bounds were {path.Bounds}.");
        using var target = new RenderTargetBitmap(new((int)Math.Ceiling(main.ClientSize.Width),
            (int)Math.Ceiling(main.ClientSize.Height)), new(96, 96));
        target.Render(main);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        var origin = path.TranslatePoint(new Point(), main)!.Value;
        var iconRect = new Rect(origin, path.Bounds.Size);
        var colors = new HashSet<SKColor>();
        for (var y = (int)Math.Floor(iconRect.Top); y < Math.Ceiling(iconRect.Bottom); y++)
        {
            for (var x = (int)Math.Floor(iconRect.Left); x < Math.Ceiling(iconRect.Right); x++)
            {
                colors.Add(pixels.GetPixel(x, y));
            }
        }
        Assert.True(colors.Count > 1, "A clickable Dock menu button must contain visible icon pixels.");
    }
}
