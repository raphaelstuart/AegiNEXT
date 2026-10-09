using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewPanelLayoutUiTests
{
    [AvaloniaTheory]
    [InlineData(260, WorkbenchTheme.DARK, "zh-CN")]
    [InlineData(260, WorkbenchTheme.LIGHT, "en-US")]
    [InlineData(580, WorkbenchTheme.DARK, "en-US")]
    [InlineData(580, WorkbenchTheme.LIGHT, "zh-CN")]
    public async Task ShrinkingTheHostKeepsControlsAndFooterVisibleByScalingThePicture(
        double width, WorkbenchTheme theme, string language)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        context.Session.UpdatePreferences(context.Session.Preferences with { Theme = theme, Language = language });
        context.Window.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        var panel = context.Window.Panels[WorkbenchPanelIds.PREVIEW];
        var surface = panel.FindControl<Grid>("VideoSurface")!;
        var canvas = panel.FindControl<EffectCanvasControl>("EffectCanvas")!;
        var quality = panel.FindControl<ComboBox>("QualityCombo")!;
        var position = panel.FindControl<Slider>("PositionSlider")!;
        var transport = panel.FindControl<Grid>("TransportRow")!;
        var originalPicture = default(Rect);
        floating.Width = width;
        try
        {
            foreach (var height in new[] { 450, 280, 220, 180, 450 })
            {
                floating.Height = height;
                floating.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                floating.UpdateLayout();
                AssertFits(surface, panel);
                AssertFits(quality, surface);
                AssertFits(panel.FindControl<Border>("ModeBadge")!, surface);
                AssertFits(position, panel);
                AssertFits(transport, panel);
                foreach (var name in new[] { "PlayButton", "TimeLabel", "MuteButton", "VolumeSlider" })
                {
                    AssertFits(panel.FindControl<Control>(name)!, panel);
                }

                var surfaceBottom = surface.TranslatePoint(new(0, surface.Bounds.Height), panel)!.Value.Y;
                var positionTop = position.TranslatePoint(default, panel)!.Value.Y;
                Assert.True(surfaceBottom <= positionTop);
                Assert.Equal(24, position.Bounds.Height);
                Assert.Equal(32, transport.Bounds.Height);
                foreach (var slider in new[] { position, panel.FindControl<Slider>("VolumeSlider")! })
                {
                    AssertFits(slider.GetVisualDescendants().OfType<Thumb>().Single(), slider);
                }

                var picture = canvas.ProjectRectangle;
                Assert.True(picture.Width > 0 && picture.Height > 0);
                Assert.True(new Rect(canvas.Bounds.Size).Contains(picture));
                Assert.Equal((double)context.Session.DocumentSnapshot.Width / context.Session.DocumentSnapshot.Height,
                    picture.Width / picture.Height, 6);
                if (originalPicture == default)
                {
                    originalPicture = picture;
                }
                else if (height == 180)
                {
                    Assert.True(picture.Height < originalPicture.Height);
                    Assert.True(picture.Width < originalPicture.Width);
                }
                else if (height == 450)
                {
                    Assert.Equal(originalPicture, picture);
                }

                AssertFooterBackground(floating, panel, transport);
            }

            floating.Height = 180;
            floating.UpdateLayout();
            using var rendered = floating.CaptureRenderedFrame();
            var point = quality.TranslatePoint(new(quality.Bounds.Width / 2, quality.Bounds.Height / 2), floating)!.Value;
            floating.MouseDown(point, MouseButton.Left);
            floating.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(quality.IsDropDownOpen);
            quality.IsDropDownOpen = false;
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void AssertFits(Control control, Control container)
    {
        var origin = control.TranslatePoint(default, container)!.Value;
        Assert.True(control.IsEffectivelyVisible);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0);
        Assert.InRange(origin.X, 0, container.Bounds.Width - control.Bounds.Width + 0.001);
        Assert.InRange(origin.Y, 0, container.Bounds.Height - control.Bounds.Height + 0.001);
    }

    private static void AssertFooterBackground(Window window, Control panel, Control transport)
    {
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        using var stream = new MemoryStream();
        frame.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        using var pixels = SKBitmap.Decode(stream);
        var origin = panel.TranslatePoint(default, window)!.Value;
        var row = transport.TranslatePoint(default, window)!.Value;
        var actual = pixels.GetPixel((int)((origin.X + 4) * window.RenderScaling),
            (int)((row.Y + transport.Bounds.Height / 2) * window.RenderScaling));
        var background = Assert.IsType<Border>(Assert.IsAssignableFrom<UserControl>(panel).Content).Background;
        var expected = Assert.IsAssignableFrom<ISolidColorBrush>(background).Color;
        Assert.Equal(new SKColor(expected.R, expected.G, expected.B, expected.A), actual);
    }
}
