using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineHeaderTypographyUiTests
{
    [AvaloniaFact]
    public async Task StyleBadgeUsesSharedMixedTextLineBoxAndSingleTriangleTogglesAllAnimations()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var trackId = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var cue = context.Session.Editor.AddSubtitle(new(0), new(3), "中文 TEST 123", trackId);
        context.Session.Editor.SetSubtitleTrackStyle(trackId, Guid.NewGuid(), "nano 中文 123", new());
        context.Session.Editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.4));
        context.Session.Editor.SetKeyframe(cue, AnimationProperty.POSITION, new(new(1), new ScenePoint(0, 0)));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var badge = timeline.GetTrackStyleBadgeRectangle(trackId)!.Value;
        var baselines = new List<double>();
        foreach (var sample in new[] { "nano", "中文字幕", "中文 ABC 123" })
        {
            using var layout = WorkbenchTextFormatting.CreateLayout(timeline, sample, 10, Brushes.White,
                badge.Height, badge.Width - 8);
            var origin = WorkbenchTextFormatting.CenteredOrigin(layout, badge);
            Assert.Equal(badge.Center.Y, origin.Y + layout.Height / 2, 5);
            baselines.Add(origin.Y + layout.TextLines[0].Baseline);
        }
        Assert.InRange(baselines.Max() - baselines.Min(), 0, 0.5);
        Assert.Equal(2, timeline.GetAnimationProperties(cue).Count);
        var header = timeline.GetTrackHeaderRectangle(trackId)!.Value;
        var triangle = timeline.TranslatePoint(new(14, header.Top + 14), context.Window)!.Value;
        context.Window.MouseDown(triangle, MouseButton.Left);
        context.Window.MouseUp(triangle, MouseButton.Left);
        Assert.True(timeline.IsTrackCollapsed(trackId));
        Assert.Empty(timeline.GetAnimationProperties(cue));
        Assert.NotNull(timeline.GetClipRectangle(cue));
        context.Window.MouseDown(triangle, MouseButton.Left);
        context.Window.MouseUp(triangle, MouseButton.Left);
        Assert.False(timeline.IsTrackCollapsed(trackId));
        Assert.Equal(2, timeline.GetAnimationProperties(cue).Count);
    }
}
