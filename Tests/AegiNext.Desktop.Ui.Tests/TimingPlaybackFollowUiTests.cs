using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimingPlaybackFollowUiTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task F8FollowsRelativePlaybackInTimelineAndPreviewWithoutEditingTheProjectOnEachTick(int mediaOrigin)
    {
        await using var context = new MainWindowTestContext(mediaStart: new(mediaOrigin));
        await context.OpenMediaAsync();
        FocusTimeline(context);
        await context.Controller.PlayAsync();

        UiTestActions.Press(context.Window, Key.F8);
        await EventuallyAsync(() => context.Session.DocumentSnapshot.Subtitles.Length == 1);
        var provisional = context.Session.DocumentSnapshot;
        var cue = Assert.Single(provisional.Subtitles);
        Assert.Equal(MediaTime.Zero, cue.Start);
        Assert.Equal(new MediaTime(1, 1000), cue.End);
        Assert.True(context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));

        for (var tick = 1; tick <= 8; tick++)
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(125));
            context.Session.Tick();
            var expectedEnd = new MediaTime(tick, 8);
            var preview = context.ViewModel.Timeline.TimingPreview;
            Assert.NotNull(preview);
            Assert.Equal(cue.Id, preview.CueId);
            Assert.Equal(cue.Start, preview.Start);
            Assert.Equal(expectedEnd, preview.End);
            Assert.Equal(expectedEnd, context.ViewModel.Timeline.Position);
            Assert.Same(provisional, context.Session.DocumentSnapshot);
            var previewDocument = context.Session.PreviewDocument;
            Assert.Equal(expectedEnd, Assert.Single(previewDocument.Subtitles).End);
            Assert.Equal(expectedEnd, Assert.Single(previewDocument.Layers).End);
        }

        UiTestActions.Press(context.Window, Key.F9);
        await EventuallyAsync(() => !context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        var completed = context.Session.DocumentSnapshot;
        Assert.Equal(new MediaTime(1), Assert.Single(completed.Subtitles).End);
        Assert.Equal(new MediaTime(1), Assert.Single(completed.Layers).End);
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.Null(context.Session.LastError);
    }

    [AvaloniaTheory]
    [InlineData("Space")]
    [InlineData("PlayButton")]
    [InlineData("Seek")]
    [InlineData("Selection")]
    public async Task OtherInputFreezesTheLastDisplayedEndAndMakesF9Ineffective(string input)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var existingId = context.Session.Editor.AddSubtitle(new(10), new(12), "Existing 中文 123",
            context.Session.CurrentTrackId);
        FocusTimeline(context);
        await context.Controller.PlayAsync();
        UiTestActions.Press(context.Window, Key.F8);
        await EventuallyAsync(() => context.Session.DocumentSnapshot.Subtitles.Length == 2);
        var afterEnter = context.Session.DocumentSnapshot;
        var activeId = afterEnter.Subtitles.Single(line => line.Id != existingId).Id;
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        context.Session.Tick();
        Assert.Equal(new MediaTime(1), context.ViewModel.Timeline.TimingPreview!.End);

        switch (input)
        {
            case "Space":
                UiTestActions.Press(context.Window, Key.Space);
                await EventuallyAsync(() => context.Controller.Snapshot.State == VideoPlaybackState.PAUSED);
                break;
            case "PlayButton":
                UiTestActions.Click(context.Window, "PlayButton");
                await EventuallyAsync(() => context.Controller.Snapshot.State == VideoPlaybackState.PAUSED);
                break;
            case "Seek":
                UiTestActions.Find<Slider>(context.Window, "PositionSlider").Value = 3;
                await EventuallyAsync(() => context.Controller.Snapshot.Position == new MediaTime(3));
                break;
            case "Selection":
                ClickRowTypeCell(context, existingId);
                Assert.Equal(existingId, context.Session.SelectedCueId);
                break;
        }

        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.False(context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        var frozen = context.Session.DocumentSnapshot;
        Assert.Equal(new MediaTime(1), frozen.Subtitles.Single(line => line.Id == activeId).End);
        Assert.Equal(new MediaTime(1), frozen.Layers.Single(layer => layer.SubtitleId == activeId).End);
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        context.Session.Tick();
        Assert.Same(frozen, context.Session.DocumentSnapshot);
        FocusTimeline(context);
        UiTestActions.Press(context.Window, Key.F9);
        Assert.Same(frozen, context.Session.DocumentSnapshot);
        Assert.Null(context.Session.LastError);
        PressUndo(context);
        Assert.Same(afterEnter, context.Session.DocumentSnapshot);
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
    }

    [AvaloniaFact]
    public async Task FollowingCreatesNoUndoEntriesAndF9AddsOnlyOneFinalTimingTransaction()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var beforeEnter = context.Session.DocumentSnapshot;
        FocusTimeline(context);
        await context.Controller.PlayAsync();
        UiTestActions.Press(context.Window, Key.F8);
        await EventuallyAsync(() => context.Session.DocumentSnapshot.Subtitles.Length == 1);
        var afterEnter = context.Session.DocumentSnapshot;

        for (var tick = 0; tick < 16; tick++)
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(125));
            context.Session.Tick();
            Assert.Same(afterEnter, context.Session.DocumentSnapshot);
        }

        UiTestActions.Press(context.Window, Key.F9);
        Assert.Equal(new MediaTime(2), Assert.Single(context.Session.DocumentSnapshot.Subtitles).End);
        PressUndo(context);
        Assert.Same(afterEnter, context.Session.DocumentSnapshot);
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.False(context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        PressUndo(context);
        Assert.Same(beforeEnter, context.Session.DocumentSnapshot);
        Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
    }

    [AvaloniaTheory]
    [InlineData(3, 1)]
    [InlineData(1, 2000)]
    public async Task FollowingStopsAtTheNextCueBoundaryAndF9CommitsThatSameLegalEnd(long nextStartNumerator,
        long nextStartDenominator)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var nextStart = new MediaTime(nextStartNumerator, nextStartDenominator);
        context.Session.Editor.AddSubtitle(nextStart, nextStart + new MediaTime(1), "Next cue", context.Session.CurrentTrackId);
        var nextCue = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        FocusTimeline(context);
        await context.Controller.PlayAsync();
        UiTestActions.Press(context.Window, Key.F8);
        await EventuallyAsync(() => context.Session.DocumentSnapshot.Subtitles.Length == 2);
        var provisional = context.Session.DocumentSnapshot;
        var activeId = provisional.Subtitles.Single(line => line.Id != nextCue.Id).Id;
        context.Clock.Advance(TimeSpan.FromMilliseconds(3500));
        context.Session.Tick();

        Assert.Same(provisional, context.Session.DocumentSnapshot);
        Assert.Equal(nextCue.Start, context.ViewModel.Timeline.TimingPreview!.End);
        Assert.Equal(nextCue.Start, context.Session.PreviewDocument.Subtitles.Single(line => line.Id == activeId).End);
        UiTestActions.Press(context.Window, Key.F9);

        Assert.Equal(nextCue.Start, context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == activeId).End);
        Assert.Equal(nextCue, context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == nextCue.Id));
        ProjectValidator.Validate(context.Session.DocumentSnapshot);
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.False(context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        Assert.Null(context.Session.LastError);
    }

    [AvaloniaFact]
    public async Task ImmediateF9ClosesTheDisplayedMinimumSpanWithoutCreatingAnotherCue()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        FocusTimeline(context);

        UiTestActions.Press(context.Window, Key.F8);
        UiTestActions.Press(context.Window, Key.F9);
        await EventuallyAsync(() => context.Session.DocumentSnapshot.Subtitles.Length == 1 &&
            !context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));

        var completed = context.Session.DocumentSnapshot;
        var cue = Assert.Single(completed.Subtitles);
        Assert.Equal(MediaTime.Zero, cue.Start);
        Assert.Equal(new MediaTime(1, 1000), cue.End);
        Assert.Equal(cue.End, Assert.Single(completed.Layers).End);
        Assert.Null(context.ViewModel.Timeline.TimingPreview);
        Assert.Null(context.Session.LastError);
        UiTestActions.Press(context.Window, Key.F9);
        Assert.Same(completed, context.Session.DocumentSnapshot);
    }

    private static void FocusTimeline(MainWindowTestContext context)
    {
        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline").Focus());
    }

    private static void PressUndo(MainWindowTestContext context)
    {
        FocusTimeline(context);
        var modifiers = OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
        UiTestActions.Press(context.Window, Key.Z, modifiers);
        Dispatcher.UIThread.RunJobs();
    }

    private static void ClickRowTypeCell(MainWindowTestContext context, Guid cueId)
    {
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == cueId);
        list.ScrollIntoView(row);
        Flush(context.Window);
        var container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(row));
        var cell = FindRowTypeCell(container, cueId);
        cell.BringIntoView();
        Flush(context.Window);
        container = Assert.IsType<ListBoxItem>(list.ContainerFromItem(row));
        cell = FindRowTypeCell(container, cueId);
        Assert.True(container.IsEffectivelyVisible);
        var point = cell.TranslatePoint(new Point(cell.Bounds.Width / 2, cell.Bounds.Height / 2), context.Window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(context.Window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, container) || hit.GetVisualAncestors().Contains(container));
        Assert.True(context.Window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBlock FindRowTypeCell(ListBoxItem container, Guid cueId) =>
        container.GetVisualDescendants().OfType<TextBlock>().Single(control =>
            control.DataContext is SubtitleRow item && item.Id == cueId && Grid.GetColumn(control) == 0 &&
            control.Text == item.ContentType);

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
            Assert.True(DateTime.UtcNow < deadline, "Timing workflow did not reach the expected state.");
            await Task.Delay(1, TestContext.Current.CancellationToken);
        }
        Dispatcher.UIThread.RunJobs();
    }
}
