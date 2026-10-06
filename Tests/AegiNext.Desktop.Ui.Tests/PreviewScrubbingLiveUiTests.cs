using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewScrubbingLiveUiTests
{
    [AvaloniaTheory]
    [InlineData(false, PreviewQuality.LOW)]
    [InlineData(true, PreviewQuality.LOW)]
    [InlineData(false, PreviewQuality.LOWEST)]
    [InlineData(true, PreviewQuality.LOWEST)]
    public async Task VideoPresentsIntermediateFramesWhilePointerRemainsPressedAndCanReturnToStart(bool useProgressBar,
        PreviewQuality quality)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { PreviewQuality = quality });
        await context.OpenMediaAsync();
        await DrainAsync(() => context.Controller.Snapshot.PresentedGeneration is not null);
        var initialGeneration = context.Controller.Snapshot.PresentedGeneration!.Value;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var slider = UiTestActions.Find<Slider>(context.Window, "PositionSlider");
        context.Window.UpdateLayout();
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        var start = useProgressBar
            ? thumb.TranslatePoint(new(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), context.Window)!.Value
            : timeline.TranslatePoint(new(timeline.HeaderWidth + 1, 8), context.Window)!.Value;
        var end = start + new Vector(useProgressBar ? (slider.Bounds.Width - thumb.Bounds.Width) * 0.6 : timeline.PixelsPerSecond * 10, 0);
        context.Window.MouseDown(start, MouseButton.Left);
        try
        {
            context.Window.MouseMove(end);
            var target = context.Session.ProjectPosition;
            Assert.True(target >= new MediaTime(5));
            await DrainAsync(() => context.Controller.Snapshot.PresentedAtPosition == target);
            Assert.True(context.Controller.Snapshot.PresentedGeneration > initialGeneration);
            Assert.True(context.Controller.Snapshot.PresentedFrameTime >= new MediaTime(5));
            Assert.True(useProgressBar ? context.ViewModel.Preview.IsScrubbing : context.ViewModel.Timeline.IsSeeking);
            Assert.True(context.ViewModel.Preview.Scene.IsInteractive);
            Assert.Equal(quality, context.ViewModel.Preview.Scene.Quality);
            var expectedSize = quality == PreviewQuality.LOWEST ? new PixelSize(568, 320) : new PixelSize(960, 540);
            Assert.Equal(expectedSize, UiTestActions.Find<EffectCanvasControl>(context.Window, "EffectCanvas").MaximumPreviewSize);
            context.Window.MouseMove(start);
            var returned = context.Session.ProjectPosition;
            Assert.InRange((double)returned.Numerator / returned.Denominator, 0, 0.1);
            await DrainAsync(() => context.Controller.Snapshot.PresentedAtPosition == returned);
            Assert.True(useProgressBar ? context.ViewModel.Preview.IsScrubbing : context.ViewModel.Timeline.IsSeeking);
        }
        finally
        {
            context.Window.MouseUp(start, MouseButton.Left);
        }
        await DrainAsync(() => !context.ViewModel.Preview.Scene.IsInteractive);
        Assert.False(context.ViewModel.Preview.IsScrubbing);
        Assert.False(context.ViewModel.Timeline.IsSeeking);
        Assert.Equal(quality, context.Session.Preferences.PreviewQuality);
    }

    private static async Task DrainAsync(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!completed())
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(DateTime.UtcNow < deadline, "The preview must present the target before mouse release.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
