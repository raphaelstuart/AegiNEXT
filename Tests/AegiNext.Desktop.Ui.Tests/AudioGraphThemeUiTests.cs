using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AudioGraphThemeUiTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void PalettePreviewRecolorsWithThemeAndPreservesTheSourcePalette(int index)
    {
        using var environment = new UiTestEnvironment();
        var palette = AudioGraphPalettes.Get(index);
        var preview = new AudioGraphPreviewControl { Palette = palette };
        var window = new Window { Width = 256, Height = 120, Content = preview, RequestedThemeVariant = ThemeVariant.Dark };
        try
        {
            window.Show();
            Flush(window);
            var dark = Pixel(preview);
            window.RequestedThemeVariant = ThemeVariant.Light;
            Flush(window);
            var light = Pixel(preview);
            Assert.True(light.Red + light.Green + light.Blue > 700);
            Assert.NotEqual(dark, light);
            Assert.Same(palette, preview.Palette);
            window.RequestedThemeVariant = ThemeVariant.Dark;
            Flush(window);
            Assert.Equal(dark, Pixel(preview));

            preview.Palette = palette with { AdaptToTheme = false };
            var custom = Pixel(preview);
            window.RequestedThemeVariant = ThemeVariant.Light;
            Flush(window);
            Assert.Equal(custom, Pixel(preview));
        }
        finally
        {
            window.Close();
        }
    }

    private static SKColor Pixel(Control control)
    {
        using var target = new RenderTargetBitmap(new((int)control.Bounds.Width, (int)control.Bounds.Height), new(96, 96));
        target.Render(control);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        return pixels.GetPixel(0, 4);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
