using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewTransportUiTests
{
    [AvaloniaFact]
    public async Task ActualPlaybackAndMuteButtonsAreSquareAndRetainVolume()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var play = UiTestActions.Find<Button>(context.Window, "PlayButton");
        Assert.Equal(32, play.Bounds.Width);
        Assert.Equal(32, play.Bounds.Height);
        Assert.IsType<MaterialIcon>(play.Content);
        Assert.Equal(context.ViewModel.Preview.PlayLabel, AutomationProperties.GetName(play));
        var initialPlayKind = Assert.IsType<MaterialIcon>(play.Content).Kind;
        UiTestActions.Click(context.Window, "PlayButton");
        await DrainAsync(() => context.ViewModel.Preview.IsPlaying);
        Assert.NotEqual(initialPlayKind, Assert.IsType<MaterialIcon>(play.Content).Kind);
        Assert.Equal(context.ViewModel.Preview.PlayLabel, AutomationProperties.GetName(play));
        UiTestActions.Click(context.Window, "PlayButton");
        await DrainAsync(() => !context.ViewModel.Preview.IsPlaying);
        Assert.Equal(initialPlayKind, Assert.IsType<MaterialIcon>(play.Content).Kind);
        var volume = UiTestActions.Find<Slider>(context.Window, "VolumeSlider");
        volume.Value = 0.37;
        var mute = UiTestActions.Find<ToggleButton>(context.Window, "MuteButton");
        Assert.Equal(32, mute.Bounds.Width);
        Assert.Equal(32, mute.Bounds.Height);
        var soundKind = Assert.IsType<MaterialIcon>(mute.Content).Kind;
        UiTestActions.Click(context.Window, "MuteButton");
        Assert.True(context.ViewModel.Preview.IsMuted);
        Assert.NotEqual(soundKind, Assert.IsType<MaterialIcon>(mute.Content).Kind);
        Assert.Equal(0.37, context.ViewModel.Preview.Volume, 6);
        UiTestActions.Press(context.Window, Key.Space);
        await DrainAsync(() => context.ViewModel.Preview.IsPlaying);
        Assert.True(context.ViewModel.Preview.IsMuted);
        UiTestActions.Press(context.Window, Key.Space);
        await DrainAsync(() => !context.ViewModel.Preview.IsPlaying);
        UiTestActions.Click(context.Window, "MuteButton");
        Assert.False(context.ViewModel.Preview.IsMuted);
        Assert.Equal(soundKind, Assert.IsType<MaterialIcon>(mute.Content).Kind);
        Assert.Equal(0.37, context.ViewModel.Preview.Volume, 6);
    }

    [AvaloniaTheory]
    [InlineData(360, true)]
    [InlineData(720, false)]
    public async Task TransportIsCenteredCompactAndVolumeFollowsMuteInNarrowAndWideHosts(double width, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        context.Session.UpdatePreferences(context.Session.Preferences with { Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT });
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        context.Window.Layouts.Float(WorkbenchPanelIds.PREVIEW);
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        floating.Width = width;
        floating.Height = 450;
        try
        {
            floating.UpdateLayout();
            var panel = context.Window.Panels[WorkbenchPanelIds.PREVIEW];
            Assert.Null(panel.FindControl<TextBlock>("FileTitle"));
            var surface = panel.FindControl<Grid>("VideoSurface")!;
            var badge = panel.FindControl<Border>("ModeBadge")!;
            Assert.Equal(context.ViewModel.Preview.FileTitle, ToolTip.GetTip(surface));
            var options = panel.FindControl<Grid>("PreviewOptions")!;
            Assert.Same(options, badge.Parent);
            Assert.Equal(HorizontalAlignment.Right, options.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Top, options.VerticalAlignment);
            var quality = panel.FindControl<ComboBox>("QualityCombo")!;
            Assert.Equal(quality.Bounds.Height, badge.Bounds.Height);
            Assert.Equal(32, badge.Bounds.Height);
            Assert.Equal(quality.TranslatePoint(default, surface)!.Value.Y, badge.TranslatePoint(default, surface)!.Value.Y);
            Assert.False(badge.IsHitTestVisible);
            var badgeOrigin = badge.TranslatePoint(default, surface)!.Value;
            Assert.InRange(surface.Bounds.Width - badgeOrigin.X - badge.Bounds.Width, 7, 9);
            Assert.InRange(badgeOrigin.Y, 7, 9);
            Assert.Equal("SDR", panel.FindControl<TextBlock>("ModeLabel")!.Text);
            var row = panel.FindControl<Grid>("TransportRow")!;
            var time = panel.FindControl<TextBlock>("TimeLabel")!;
            Assert.Equal(HorizontalAlignment.Center, time.HorizontalAlignment);
            Assert.Equal(VerticalAlignment.Center, time.VerticalAlignment);
            var mute = panel.FindControl<ToggleButton>("MuteButton")!;
            var volume = panel.FindControl<Slider>("VolumeSlider")!;
            var muteOrigin = mute.TranslatePoint(default, floating)!.Value;
            var volumeOrigin = volume.TranslatePoint(default, floating)!.Value;
            Assert.True(volumeOrigin.X >= muteOrigin.X + mute.Bounds.Width);
            var position = panel.FindControl<Slider>("PositionSlider")!;
            Assert.Equal(24, position.Bounds.Height);
            foreach (var slider in new[] { position, volume })
            {
                var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
                var thumbOrigin = thumb.TranslatePoint(default, slider)!.Value;
                Assert.Equal(20, thumb.Bounds.Height);
                Assert.Equal(20, thumb.Bounds.Width);
                Assert.InRange(thumbOrigin.Y, 0, slider.Bounds.Height - thumb.Bounds.Height);
                Assert.Equal(slider.Bounds.Height / 2, thumbOrigin.Y + thumb.Bounds.Height / 2, 5);
            }
            var positionOrigin = position.TranslatePoint(default, floating)!.Value;
            var rowOrigin = row.TranslatePoint(default, floating)!.Value;
            Assert.InRange(rowOrigin.Y - positionOrigin.Y - position.Bounds.Height, 0, 2);
            Assert.Equal(32, row.Bounds.Height);
            Capture(floating, $"preview-transport-{width}.png");
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task RegisteredUnsavedDialogFitsItsContentsAndPreservesTheChosenResult(int expected)
    {
        await using var context = new MainWindowTestContext();
        var dialog = new UnsavedProjectDialog();
        try
        {
            context.WindowRegistry.RegisterAuxiliary(dialog);
            var answer = dialog.ShowDialog<int>(context.Window);
            dialog.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(SizeToContent.Height, dialog.SizeToContent);
            Assert.InRange(dialog.ClientSize.Height, 100, 200);
            var save = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "SaveButton");
            var bottom = save.TranslatePoint(new Point(0, save.Bounds.Height), dialog)!.Value.Y;
            Assert.InRange(dialog.ClientSize.Height - bottom, 19, 21);
            Capture(dialog, "unsaved-dialog-auto-height.png");
            var name = expected switch { 0 => "CancelButton", 1 => "SaveButton", _ => "DiscardButton" };
            var button = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.Name == name);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), dialog)!.Value;
            dialog.MouseDown(point, MouseButton.Left);
            dialog.MouseUp(point, MouseButton.Left);
            Assert.Equal(expected, await answer);
        }
        finally
        {
            dialog.Close(0);
        }
    }

    private static async Task DrainAsync(Func<bool> complete)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!complete())
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
