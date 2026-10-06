using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleSelectionTimelineCenterUiTests
{
    [AvaloniaTheory]
    [InlineData(8000, 10000, 96)]
    [InlineData(0, 1000, 96)]
    [InlineData(18000, 20000, 96)]
    [InlineData(4000, 18000, 256)]
    public async Task ActualRowClickCentersTheCueWithoutChangingZoomFocusOrProject(int startMs, int endMs, double zoom)
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context, zoom, (startMs, endMs));
        var original = context.Session.DocumentSnapshot;
        var cue = Assert.Single(original.Subtitles);
        context.ViewModel.Timeline.ViewStart = 2;
        Flush(context.Window);

        ClickRowTypeCell(context, cue.Id);

        AssertCentered(context, timeline, cue, zoom);
        Assert.Equal(cue.Id, context.Session.SelectedCueId);
        Assert.Equal(cue.Id, Assert.Single(context.Session.SelectedSubtitleIds));
        Assert.Equal(cue.Id, Assert.Single(context.ViewModel.Timeline.SelectedLayerIds));
        AssertListHasFocus(context);
        AssertUnchanged(context, original);
        if (startMs == 0)
        {
            Assert.Equal(0, timeline.ViewStart);
        }
        if (endMs == 20000)
        {
            Assert.True(timeline.ViewStart + timeline.Viewport.VisibleDuration > 20);
        }
        if (zoom == 256)
        {
            Assert.True(timeline.GetClipRectangle(cue.Id)!.Value.Width > timeline.Viewport.Width);
        }
    }

    [AvaloniaFact]
    public async Task ShiftRowSelectionCentersItsPrimaryCueAndPreservesTheWholeSelectedRange()
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context, 96, (4000, 6000), (10000, 12000), (16000, 18000));
        var original = context.Session.DocumentSnapshot;
        var lines = original.Subtitles;
        ClickRowTypeCell(context, lines[0].Id);

        ClickRowTypeCell(context, lines[2].Id, RawInputModifiers.Shift);

        Assert.Equal(lines.Select(line => line.Id).Order(), context.Session.SelectedSubtitleIds.Order());
        Assert.Equal(lines.Select(line => line.Id).Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        Assert.Equal(lines[2].Id, context.Session.SelectedCueId);
        AssertCentered(context, timeline, lines[2], 96);
        AssertListHasFocus(context);
        AssertUnchanged(context, original);
    }

    [AvaloniaFact]
    public async Task UpAndDownKeysCenterTheNewPrimaryRowAndKeepKeyboardFocusInTheList()
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context, 96, (4000, 6000), (10000, 12000), (16000, 18000));
        var original = context.Session.DocumentSnapshot;
        ClickRowTypeCell(context, original.Subtitles[0].Id);

        UiTestActions.Press(context.Window, Key.Down);
        Flush(context.Window);

        Assert.Equal(original.Subtitles[1].Id, context.Session.SelectedCueId);
        Assert.Equal(original.Subtitles[1].Id, Assert.Single(context.Session.SelectedSubtitleIds));
        AssertCentered(context, timeline, original.Subtitles[1], 96);
        AssertListHasFocus(context);
        UiTestActions.Press(context.Window, Key.Up);
        Flush(context.Window);
        Assert.Equal(original.Subtitles[0].Id, context.Session.SelectedCueId);
        AssertCentered(context, timeline, original.Subtitles[0], 96);
        AssertListHasFocus(context);
        AssertUnchanged(context, original);
    }

    [AvaloniaFact]
    public async Task FocusingTheAlreadyPrimaryContentInputRecentersWithoutCollapsingTheSubtitleSelection()
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context, 96, (6000, 8000), (12000, 14000));
        var original = context.Session.DocumentSnapshot;
        var first = original.Subtitles[0];
        var primary = original.Subtitles[1];
        ClickRowTypeCell(context, first.Id);
        ClickRowTypeCell(context, primary.Id, RawInputModifiers.Shift);
        context.ViewModel.Timeline.ViewStart = 0;
        Flush(context.Window);
        var list = VisibleList(context);
        var container = RowContainer(context, list, primary.Id);
        var input = container.GetVisualDescendants().OfType<TextBox>().Single(box =>
            box.AcceptsReturn && box.DataContext is SubtitleRow row && row.Id == primary.Id);

        ClickControl(context.Window, container, input);

        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(primary.Id, context.Session.SelectedCueId);
        Assert.Equal(new[] { first.Id, primary.Id }.Order(), context.Session.SelectedSubtitleIds.Order());
        Assert.Equal(new[] { first.Id, primary.Id }.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
        AssertCentered(context, timeline, primary, 96);
        Assert.Equal(primary.Text, input.Text);
        AssertUnchanged(context, original);
    }

    [AvaloniaFact]
    public async Task RejectedRowSelectionWithAnInvalidTimeDraftDoesNotMoveTheViewport()
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context, 96, (8000, 10000), (14000, 16000));
        var original = context.Session.DocumentSnapshot;
        var first = original.Subtitles[0];
        var other = original.Subtitles[1];
        ClickRowTypeCell(context, first.Id);
        var list = VisibleList(context);
        var container = RowContainer(context, list, first.Id);
        var input = container.GetVisualDescendants().OfType<TextBox>().Single(box =>
            box.DataContext is SubtitleRow row && row.Id == first.Id && Grid.GetColumn(box) == 1);
        var originalText = input.Text;
        try
        {
            ClickControl(context.Window, container, input);
            input.SelectAll();
            context.Window.KeyTextInput("invalid");
            Flush(context.Window);
            var before = timeline.Viewport;

            ClickRowTypeCell(context, other.Id);

            Assert.Equal(first.Id, context.Session.SelectedCueId);
            Assert.Equal(first.Id, Assert.Single(context.Session.SelectedSubtitleIds));
            Assert.Equal(before.StartSeconds, timeline.ViewStart, 7);
            Assert.Equal(before.StartSeconds, context.ViewModel.Timeline.ViewStart, 7);
            Assert.Equal(before.PixelsPerSecond, timeline.PixelsPerSecond, 7);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.False(string.IsNullOrWhiteSpace(context.ViewModel.Subtitles.ValidationError));
        }
        finally
        {
            input.Text = originalText;
            Dispatcher.UIThread.RunJobs();
            Assert.True(context.ViewModel.TryCommitDrafts());
        }
    }

    [AvaloniaFact]
    public async Task SelectingAFarCueDuringPlaybackKeepsItCenteredAcrossTicksUntilExplicitTransportResumesFollowing()
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context, 96, (14000, 16000));
        var original = context.Session.DocumentSnapshot;
        var cue = Assert.Single(original.Subtitles);
        await context.Controller.PlayAsync();

        ClickRowTypeCell(context, cue.Id);

        AssertCentered(context, timeline, cue, 96);
        var centeredStart = timeline.ViewStart;
        Assert.True(centeredStart > 1);
        for (var tick = 0; tick < 3; tick++)
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(250));
            context.Session.Tick();
            Flush(context.Window);
            Assert.Equal(centeredStart, timeline.ViewStart, 7);
        }
        AssertListHasFocus(context);
        AssertUnchanged(context, original);

        UiTestActions.Press(context.Window, Key.Space);
        await EventuallyAsync(() => context.Controller.Snapshot.State == VideoPlaybackState.PAUSED);
        UiTestActions.Press(context.Window, Key.Space);
        await EventuallyAsync(() => context.Controller.Snapshot.State == VideoPlaybackState.PLAYING);
        context.Session.Tick();
        Flush(context.Window);
        Assert.True(timeline.ViewStart < centeredStart);
        Assert.True(context.ViewModel.Timeline.Position >= MediaTime.Zero);
        AssertUnchanged(context, original);
    }

    [AvaloniaFact]
    public async Task DirectTimelineClipClickKeepsTheCurrentViewportAndDragSelectionBehavior()
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context, 96, (9000, 11000));
        var original = context.Session.DocumentSnapshot;
        var cue = Assert.Single(original.Subtitles);
        context.Window.Layouts.Activate(WorkbenchPanelIds.TIMELINE);
        Flush(context.Window);
        var zoom = timeline.Viewport.Width / 6;
        context.ViewModel.Timeline.PixelsPerSecond = zoom;
        context.ViewModel.Timeline.ViewStart = 8;
        Flush(context.Window);
        var viewport = timeline.Viewport;
        var point = timeline.TranslatePoint(timeline.GetClipRectangle(cue.Id)!.Value.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));

        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Flush(context.Window);

        Assert.Equal(cue.Id, context.Session.SelectedCueId);
        Assert.Equal(viewport.StartSeconds, timeline.ViewStart, 7);
        Assert.Equal(viewport.PixelsPerSecond, timeline.PixelsPerSecond, 7);
        Assert.False(timeline.HasActiveDrag);
        AssertUnchanged(context, original);
    }

    [AvaloniaFact]
    public async Task MainWindowRowClickCentersTheFloatingTimelineWithoutMovingListFocus()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context, 96, (14000, 16000));
        var original = context.Session.DocumentSnapshot;
        context.Window.Layouts.Float(WorkbenchPanelIds.TIMELINE);
        Flush(context.Window);
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            floating.Width = 900;
            floating.Height = 360;
            Flush(floating);
            var timeline = UiTestActions.Find<SubtitleTimelineControl>(floating, "Timeline");
            Assert.Contains(floating, context.WindowRegistry.Windows);

            ClickRowTypeCell(context, original.Subtitles[0].Id);
            Flush(floating);

            AssertCentered(context, timeline, original.Subtitles[0], 96);
            AssertListHasFocus(context);
            AssertUnchanged(context, original);
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(floating.IsVisible);
        }
    }

    [AvaloniaFact]
    public async Task SelectionBeforeTheFloatingViewportHasWidthCentersAfterItsSizeBecomesAvailable()
    {
        await using var context = new MainWindowTestContext();
        await PrepareAsync(context, 96, (14000, 16000));
        var original = context.Session.DocumentSnapshot;
        context.Window.Layouts.Float(WorkbenchPanelIds.TIMELINE);
        Flush(context.Window);
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            floating.Width = 900;
            floating.Height = 360;
            Flush(floating);
            var timeline = UiTestActions.Find<SubtitleTimelineControl>(floating, "Timeline");
            timeline.Width = 0;
            Flush(floating);
            Assert.Equal(0, context.ViewModel.Timeline.Viewport.Width);

            ClickRowTypeCell(context, original.Subtitles[0].Id);

            Assert.Equal(original.Subtitles[0].Id, context.Session.SelectedCueId);
            Assert.Equal(0, context.ViewModel.Timeline.Viewport.Width);
            AssertListHasFocus(context);
            AssertUnchanged(context, original);
            timeline.ClearValue(SubtitleTimelineControl.WidthProperty);
            Flush(floating);
            Assert.True(context.ViewModel.Timeline.Viewport.Width > 0);
            AssertCentered(context, timeline, original.Subtitles[0], 96);
            AssertListHasFocus(context);
            AssertUnchanged(context, original);
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(floating.IsVisible);
        }
    }

    private static async Task<SubtitleTimelineControl> PrepareAsync(MainWindowTestContext context, double zoom,
        params (int StartMs, int EndMs)[] ranges)
    {
        await context.OpenMediaAsync();
        var lines = ranges.Select((range, index) => new SubtitleLine
        {
            Start = new(range.StartMs, 1000), End = new(range.EndMs, 1000), Text = $"字幕 {index + 1} ABC 123"
        }).ToArray();
        context.Session.Editor.Reset(context.Session.DocumentSnapshot with
        {
            Subtitles = [.. lines],
            Layers = [.. lines.Select(line => new ProjectLayer
            {
                Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            })]
        });
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.IsClassicTimingEnabled = false;
        context.ViewModel.Timeline.PixelsPerSecond = zoom;
        context.ViewModel.Timeline.ViewStart = 0;
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        Flush(context.Window);
        Assert.True(context.ViewModel.Timeline.Viewport.Width > 0);
        Assert.False(context.Session.Editor.CanUndo);
        return timeline;
    }

    private static ListBox VisibleList(MainWindowTestContext context)
    {
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        Flush(context.Window);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        Assert.True(list.IsEffectivelyVisible);
        return list;
    }

    private static ListBoxItem RowContainer(MainWindowTestContext context, ListBox list, Guid cueId)
    {
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == cueId);
        list.ScrollIntoView(row);
        Flush(context.Window);
        var container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(row));
        container.BringIntoView();
        Flush(context.Window);
        container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(row));
        Assert.True(container.IsEffectivelyVisible);
        return container;
    }

    private static void ClickRowTypeCell(MainWindowTestContext context, Guid cueId,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var list = VisibleList(context);
        var container = RowContainer(context, list, cueId);
        var cell = container.GetVisualDescendants().OfType<TextBlock>().Single(control =>
            control.IsEffectivelyVisible && control.GetVisualParent() is Grid grid && grid.ColumnDefinitions.Count == 5 &&
            control.DataContext is SubtitleRow row && row.Id == cueId && Grid.GetColumn(control) == 0 &&
            control.Text == row.ContentType);
        ClickControl(context.Window, container, cell, modifiers);
    }

    private static void ClickControl(Window window, ListBoxItem container, Control cell,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, container) || hit.GetVisualAncestors().Contains(container));
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(window);
    }

    private static void AssertCentered(MainWindowTestContext context, SubtitleTimelineControl timeline,
        SubtitleLine cue, double zoom)
    {
        var midpoint = ((double)cue.Start.Numerator / cue.Start.Denominator + (double)cue.End.Numerator / cue.End.Denominator) / 2;
        var expectedStart = Math.Max(0, midpoint - timeline.Viewport.VisibleDuration / 2);
        Assert.Equal(expectedStart, context.ViewModel.Timeline.ViewStart, 7);
        Assert.Equal(expectedStart, timeline.ViewStart, 7);
        Assert.Equal(zoom, context.ViewModel.Timeline.PixelsPerSecond, 7);
        Assert.Equal(zoom, timeline.PixelsPerSecond, 7);
        if (expectedStart > 0)
        {
            Assert.Equal((timeline.HeaderWidth + timeline.Bounds.Width) / 2,
                timeline.GetClipRectangle(cue.Id)!.Value.Center.X, 6);
        }
    }

    private static void AssertListHasFocus(MainWindowTestContext context)
    {
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var focused = Assert.IsAssignableFrom<Visual>(context.Window.FocusManager!.GetFocusedElement());
        Assert.True(ReferenceEquals(focused, list) || focused.GetVisualAncestors().Contains(list));
    }

    private static void AssertUnchanged(MainWindowTestContext context, ProjectDocument document)
    {
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Session.Editor.CanRedo);
        Assert.Null(context.Session.LastError);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Dispatcher.UIThread.RunJobs();
            Assert.True(DateTime.UtcNow < deadline, "Playback did not reach the expected state.");
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
