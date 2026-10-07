using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Controls;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证同窗替换媒体后的时间线视口和鼠标交互。</summary>
public sealed class TimelineMediaReplacementUiTests
{
    /// <summary>越界起点和过小缩放不能阻止短视频定位、滚动或总览缩放。</summary>
    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task ReplacingALongVideoWithAShortVideoKeepsTheTimelineWithinTheProjectAndSeekable(bool zoomedOut, bool updateProject)
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.Viewport = timeline.Viewport with
        {
            StartSeconds = zoomedOut ? 0 : 3600,
            PixelsPerSecond = zoomedOut ? timeline.Viewport.Width / 7200 : 200
        };
        context.ViewModel.Timeline.SuspendPlaybackFollow();
        var original = timeline.Viewport;
        Assert.Equal(7200, context.ViewModel.Timeline.FullDuration);
        Assert.Equal(zoomedOut ? 0 : 3600, timeline.ViewStart);
        await OpenReplacementAsync(context, "short.media", updateProject);

        Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.Equal(new MediaTime(20), context.Controller.Snapshot.Duration);
        Assert.Equal(20, context.ViewModel.Timeline.FullDuration);
        Assert.Equal(context.ViewModel.Timeline.Viewport, timeline.Viewport);
        Assert.InRange(timeline.ViewStart, 0, Math.Max(0, 20 - timeline.VisibleDuration));
        Assert.InRange(timeline.VisibleDuration, 0, 20.000000001);
        Assert.Equal(Math.Max(original.PixelsPerSecond, timeline.Viewport.Width / 20), timeline.PixelsPerSecond, 8);
        Assert.True(context.ViewModel.Timeline.IsPlaybackFollowEnabled);

        var expected = new MediaTime((long)Math.Ceiling(timeline.ViewStart) + 1);
        await SeekWithPointerAsync(context, timeline, expected);
        var generation = context.Controller.Snapshot.PresentedGeneration;
        await SeekWithPointerAsync(context, timeline, expected + new MediaTime(1));
        Assert.True(context.Controller.Snapshot.PresentedGeneration > generation);

        var document = context.Session.DocumentSnapshot;
        var position = context.Session.ProjectPosition;
        var point = timeline.TranslatePoint(new Point(timeline.HeaderWidth + timeline.Viewport.Width / 2, 8), context.Window)!.Value;
        var scale = timeline.PixelsPerSecond;
        context.Window.MouseWheel(point, new(0, 4), RawInputModifiers.Control);
        Assert.True(timeline.PixelsPerSecond > scale);
        var start = timeline.ViewStart;
        context.Window.MouseWheel(point, new(-2, 0), RawInputModifiers.None);
        Assert.True(timeline.ViewStart > start);

        var overview = UiTestActions.Find<TimelineOverviewControl>(context.Window, "TimelineMinimap");
        var rectangle = overview.ViewportRectangle;
        var edge = overview.TranslatePoint(new(rectangle.Right, rectangle.Center.Y), context.Window)!.Value;
        scale = timeline.PixelsPerSecond;
        Drag(context, edge, edge - new Vector(overview.Bounds.Width / 10, 0));
        Assert.True(timeline.PixelsPerSecond > scale);
        var center = overview.TranslatePoint(overview.ViewportRectangle.Center, context.Window)!.Value;
        start = timeline.ViewStart;
        Drag(context, center, center + new Vector(overview.Bounds.Width / 50, 0));
        Assert.True(timeline.ViewStart > start);
        var navigated = timeline.Viewport;
        Assert.False(context.ViewModel.Timeline.IsPlaybackFollowEnabled);

        await context.Controller.PlayAsync();
        for (var tick = 0; tick < 4; tick++)
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(125));
            context.Session.Tick();
            Assert.Equal(navigated, timeline.Viewport);
            Assert.Equal(navigated, context.ViewModel.Timeline.Viewport);
        }
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.Equal(position + new MediaTime(1, 2), context.Session.ProjectPosition);
    }

    /// <summary>短视频不能缩掉仍可编辑的后段字幕范围。</summary>
    [AvaloniaFact]
    public async Task ReplacingMediaPreservesAViewportWithinLongerSubtitleContent()
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        context.Session.Editor.AddSubtitle(new(110), new(120), "Later subtitle");
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.Viewport = timeline.Viewport with { StartSeconds = 60, PixelsPerSecond = 200 };
        var original = timeline.Viewport;
        await OpenReplacementAsync(context, "short.media");

        Assert.Equal(new MediaTime(20), context.Controller.Snapshot.Duration);
        Assert.Equal(120, context.ViewModel.Timeline.FullDuration);
        Assert.Equal(original, timeline.Viewport);
        Assert.Equal(original, context.ViewModel.Timeline.Viewport);
        Assert.Equal(new MediaTime(120), Assert.Single(context.Session.DocumentSnapshot.Subtitles).End);
    }

    /// <summary>失败替换恢复旧媒体时保留用户导航状态。</summary>
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedMediaReplacementRestoresTheOriginalMediaAndViewport(bool updateProject)
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.Viewport = timeline.Viewport with { StartSeconds = 3600, PixelsPerSecond = 200 };
        context.ViewModel.Timeline.SuspendPlaybackFollow();
        var original = timeline.Viewport;
        var path = context.Controller.Snapshot.FilePath;
        var document = context.Session.DocumentSnapshot;
        await Assert.ThrowsAsync<InvalidDataException>(() => OpenReplacementAsync(context, "failed.media", updateProject));

        Assert.Equal(path, context.Controller.Snapshot.FilePath);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.Equal(new MediaTime(7200), context.Controller.Snapshot.Duration);
        Assert.Equal(original, timeline.Viewport);
        Assert.Equal(original, context.ViewModel.Timeline.Viewport);
        Assert.False(context.ViewModel.Timeline.IsPlaybackFollowEnabled);
        Assert.False(context.Session.IsProjectBusy);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    /// <summary>媒体时长缩短后，预览滑块能拖动定位并保持释放时的位置。</summary>
    [AvaloniaFact]
    public async Task PreviewSliderRemainsSeekableAfterReplacingALongVideoWithAShortVideo()
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        await context.Controller.SeekAsync(new(3600));
        context.Session.Tick();
        Assert.Equal(3600, context.ViewModel.Preview.Position);
        await OpenReplacementAsync(context, "short.media");

        var slider = UiTestActions.Find<Slider>(context.Window, "PositionSlider");
        var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
        Assert.True(slider.IsEnabled);
        Assert.Equal(20, slider.Maximum);
        var start = thumb.TranslatePoint(new(thumb.Bounds.Width / 2, thumb.Bounds.Height / 2), context.Window)!.Value;
        var end = start + new Vector((slider.Bounds.Width - thumb.Bounds.Width) * 0.6, 0);
        context.Window.MouseDown(start, MouseButton.Left);
        MediaTime target;
        try
        {
            context.Window.MouseMove(end);
            target = context.Session.ProjectPosition;
            Assert.InRange((double)target.Numerator / target.Denominator, 5, 19);
            Assert.True(context.ViewModel.Preview.IsScrubbing);
            await DrainAsync(() => context.Controller.Snapshot.PresentedAtPosition == target);
        }
        finally
        {
            context.Window.MouseUp(end, MouseButton.Left);
        }
        await DrainAsync(() => context.Controller.Snapshot.Position == target &&
            context.Controller.Snapshot.PresentedAtPosition == target && !context.ViewModel.Preview.Scene.IsInteractive);
        var document = context.Session.DocumentSnapshot;
        for (var tick = 0; tick < 4; tick++)
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(125));
            context.Session.Tick();
            Assert.Equal(target, context.Session.ProjectPosition);
            Assert.Equal((double)target.Numerator / target.Denominator, slider.Value, 8);
        }
        Assert.False(context.ViewModel.Preview.IsScrubbing);
        Assert.False(context.ViewModel.Timeline.IsSeeking);
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    private static MainWindowTestContext CreateContext()
    {
        var longMedia = true;
        var timestamps = Enumerable.Range(0, 21).Select(second => second * 1000L).ToArray();
        return new(mediaProbe: (path, _) =>
        {
            if (Path.GetFileName(path) == "failed.media")
            {
                return Task.FromException<VideoPreviewMedia>(new InvalidDataException("Replacement probe failed."));
            }
            longMedia = Path.GetFileName(path) != "short.media";
            return Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero,
                longMedia ? new(7200) : new(20), VideoWidth: 1, VideoHeight: 1));
        }, videoSourceFactory: () => new PreviewTestSource(1,
            longMedia ? [.. timestamps, 3600000, 7200000] : timestamps));
    }

    private static async Task OpenReplacementAsync(MainWindowTestContext context, string fileName, bool updateProject = true)
    {
        var path = Path.Combine(Path.GetDirectoryName(context.Controller.Snapshot.FilePath)!, fileName);
        await File.WriteAllBytesAsync(path, new byte[20], TestContext.Current.CancellationToken);
        await context.Window.OpenMediaAsync(path, updateProject);
        context.Window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task SeekWithPointerAsync(MainWindowTestContext context, SubtitleTimelineControl timeline, MediaTime expected)
    {
        Assert.True(expected < new MediaTime(20));
        var point = timeline.TranslatePoint(new Point(timeline.HeaderWidth +
            ((double)expected.Numerator / expected.Denominator - timeline.ViewStart) * timeline.PixelsPerSecond, 8), context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left);
        try
        {
            Assert.True(timeline.IsSeeking);
            Assert.Equal(expected, context.Session.ProjectPosition);
            await DrainAsync(() => context.Controller.Snapshot.PresentedAtPosition == expected);
            Assert.Equal(expected, context.Controller.Snapshot.PresentedFrameTime);
        }
        finally
        {
            context.Window.MouseUp(point, MouseButton.Left);
        }
        await DrainAsync(() => context.Controller.Snapshot.Position == expected &&
            context.Controller.Snapshot.PresentedAtPosition == expected && !context.ViewModel.Preview.Scene.IsInteractive);
        Assert.Equal(expected, timeline.Position);
        Assert.False(timeline.HasActiveDrag);
        Assert.False(context.ViewModel.Timeline.IsSeeking);
    }

    private static void Drag(MainWindowTestContext context, Point start, Point end)
    {
        context.Window.MouseDown(start, MouseButton.Left);
        context.Window.MouseMove(end);
        context.Window.MouseUp(end, MouseButton.Left);
    }

    private static async Task DrainAsync(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!completed())
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(DateTime.UtcNow < deadline, "Timeline seek did not present the short video's requested frame.");
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
