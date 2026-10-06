using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

internal static class TimelineTrackTestActions
{
    internal static ContextMenu OpenMenu(MainWindowTestContext context, Guid trackId)
    {
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var header = timeline.GetTrackHeaderRectangle(trackId)!.Value;
        timeline.SetViewport(timeline.Viewport with
        {
            VerticalOffset = Math.Max(0, timeline.Viewport.VerticalOffset + header.Top - 24)
        }, context.ViewModel.Timeline.FullDuration);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        header = timeline.GetTrackHeaderRectangle(trackId)!.Value;
        var point = timeline.TranslatePoint(new Point(header.Center.X, header.Top + 12), context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Right);
        context.Window.MouseUp(point, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
        var panel = Assert.Single(timeline.GetVisualAncestors().OfType<TimelinePanelView>());
        return panel.TrackMenu;
    }

    internal static MenuItem Item(ContextMenu menu, string name) => menu.Items.OfType<MenuItem>().Single(item => item.Name == name);

    internal static void Execute(MenuItem item)
    {
        Assert.True(item.Command!.CanExecute(null));
        item.Command.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }
}
