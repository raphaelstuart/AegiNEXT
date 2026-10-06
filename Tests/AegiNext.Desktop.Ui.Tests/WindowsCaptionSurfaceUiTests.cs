using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WindowsCaptionSurfaceUiTests
{
    [AvaloniaFact]
    public void NativeCaptionApertureLeavesZeroAlphaWhileTitleAndBodyStayOpaque()
    {
        var window = CreateWindow();
        try
        {
            window.Show();
            FlushLayout(window);
            using var surface = new WindowsCaptionSurface(window);
            surface.Update(new(window.ClientSize.Width - 146, 0, 146, 40));

            using var pixels = Render(window);
            Assert.Equal(0, pixels.GetPixel(pixels.Width - 70, 20).Alpha);
            Assert.Equal(255, pixels.GetPixel(20, 20).Alpha);
            Assert.Equal(255, pixels.GetPixel(pixels.Width - 70, 80).Alpha);
            Assert.Equal(new[] { WindowTransparencyLevel.Transparent }, window.TransparencyLevelHint);

            surface.Update(default);
            using var fullScreenPixels = Render(window);
            Assert.Equal(255, fullScreenPixels.GetPixel(fullScreenPixels.Width - 70, 20).Alpha);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NativeCaptionSurfacePreservesExistingClipAndRestoresHostPolicy()
    {
        var window = CreateWindow();
        var originalClip = new RectangleGeometry(new Rect(10, 10, 370, 210));
        window.Clip = originalClip;
        var originalTransparency = window.TransparencyLevelHint;
        try
        {
            window.Show();
            FlushLayout(window);
            using (var surface = new WindowsCaptionSurface(window))
            {
                surface.Update(new(254, 0, 146, 40));
                using var pixels = Render(window);
                Assert.Equal(0, pixels.GetPixel(5, 80).Alpha);
                Assert.Equal(0, pixels.GetPixel(300, 20).Alpha);
                Assert.Equal(255, pixels.GetPixel(100, 80).Alpha);
            }

            Assert.Same(originalClip, window.Clip);
            Assert.Same(originalTransparency, window.TransparencyLevelHint);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow()
    {
        var content = new Grid { RowDefinitions = new("40,*") };
        content.Children.Add(new Border { Background = Brushes.DarkSlateBlue });
        var body = new Border { Background = Brushes.DarkSlateGray };
        Grid.SetRow(body, 1);
        content.Children.Add(body);
        return new()
        {
            Width = 400,
            Height = 240,
            SizeToContent = SizeToContent.Manual,
            Background = Brushes.DarkRed,
            Content = content
        };
    }

    private static void FlushLayout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static SKBitmap Render(Window window)
    {
        using var target = new RenderTargetBitmap(new((int)Math.Ceiling(window.ClientSize.Width),
            (int)Math.Ceiling(window.ClientSize.Height)), new(96, 96));
        target.Render(window);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
