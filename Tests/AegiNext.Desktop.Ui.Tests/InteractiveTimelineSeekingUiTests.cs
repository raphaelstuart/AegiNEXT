using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class InteractiveTimelineSeekingUiTests
{
    [AvaloniaFact]
    public async Task HandledRulerPointerStartsInteractivePreviewAndReleaseRestoresExactTargetAndQuality()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        var point = timeline.TranslatePoint(new Point(timeline.HeaderWidth + timeline.PixelsPerSecond, 8), context.Window)!.Value;
        context.Window.MouseDown(point, MouseButton.Left);
        Assert.True(timeline.IsSeeking);
        Assert.True(context.ViewModel.Timeline.IsSeeking);
        Assert.True(context.ViewModel.Preview.Scene.IsInteractive);
        var finalPoint = point + new Vector(timeline.PixelsPerSecond * 4, 0);
        for (var index = 1; index <= 20; index++)
        {
            context.Window.MouseMove(point + (finalPoint - point) * (index / 20d));
        }
        Assert.Equal(new MediaTime(5), context.Session.ProjectPosition);
        context.Window.MouseUp(finalPoint, MouseButton.Left);
        Assert.False(context.ViewModel.Timeline.IsSeeking);
        Assert.False(context.ViewModel.Preview.Scene.IsInteractive);
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (context.Controller.Snapshot.Position != new MediaTime(5))
        {
            Assert.True(DateTime.UtcNow < deadline);
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
        Assert.Equal(new MediaTime(5), context.Session.ProjectPosition);
    }
}
