using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClassicTimingFocusUiTests
{
    [AvaloniaTheory]
    [InlineData(MouseButton.Left, 4)]
    [InlineData(MouseButton.Right, 4)]
    [InlineData(MouseButton.Left, 1)]
    [InlineData(MouseButton.Right, 1)]
    [InlineData(MouseButton.Left, 2)]
    [InlineData(MouseButton.Right, 2)]
    public async Task ClassicClickPreservesTheSubtitleInputFocusAndAllowsImmediateTyping(MouseButton button, int column)
    {
        await using var context = new MainWindowTestContext();
        var (timeline, cue, input) = await PrepareAsync(context, column);
        input.CaretIndex = 5;
        input.SelectionStart = 2;
        input.SelectionEnd = 5;
        var caret = input.CaretIndex;
        var selectionStart = input.SelectionStart;
        var selectionEnd = input.SelectionEnd;
        var lostFocusCount = 0;
        input.LostFocus += (_, _) => lostFocusCount++;
        var target = new MediaTime(button == MouseButton.Left ? 7137 : 11137, 1000);

        ClickTimeline(context.Window, timeline, cue.Id, target, button);

        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        Assert.True(input.IsFocused);
        Assert.Equal(0, lostFocusCount);
        var edited = context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == cue.Id);
        Assert.Equal(button == MouseButton.Left ? target : cue.Start, edited.Start);
        Assert.Equal(button == MouseButton.Right ? target : cue.End, edited.End);
        Assert.Equal(cue.Id, context.Session.SelectedCueId);
        Assert.Equal(cue.Text, edited.Text);
        Assert.False(timeline.HasActiveDrag);
        Assert.Null(context.Session.LastError);
        if (column == 4)
        {
            Assert.Equal(caret, input.CaretIndex);
            Assert.Equal(selectionStart, input.SelectionStart);
            Assert.Equal(selectionEnd, input.SelectionEnd);
        }

        var text = input.Text!;
        var begin = Math.Min(input.SelectionStart, input.SelectionEnd);
        var end = Math.Max(input.SelectionStart, input.SelectionEnd);
        context.Window.KeyTextInput("继续");
        Flush(context.Window);
        Assert.Equal(text[..begin] + "继续" + text[end..], input.Text);
        Assert.Same(input, context.Window.FocusManager.GetFocusedElement());
        context.ViewModel.Subtitles.Rows.Single(row => row.Id == cue.Id).Accept(edited);
        Assert.True(context.Session.Editor.Undo());
        Assert.Equal(cue, context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == cue.Id));
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    public async Task RejectedClassicTimingAlsoPreservesTheSubtitleContentFocusAndSelection(MouseButton button)
    {
        await using var context = new MainWindowTestContext();
        var (timeline, cue, input) = await PrepareAsync(context, 4);
        input.CaretIndex = 5;
        input.SelectionStart = 2;
        input.SelectionEnd = 5;
        var caret = input.CaretIndex;
        var target = button == MouseButton.Left ? cue.End : cue.Start;
        var original = context.Session.DocumentSnapshot;

        ClickTimeline(context.Window, timeline, cue.Id, target, button);

        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(caret, input.CaretIndex);
        Assert.Equal(2, input.SelectionStart);
        Assert.Equal(5, input.SelectionEnd);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.NotNull(context.Session.LastError);
        Assert.False(timeline.HasActiveDrag);
    }

    [AvaloniaFact]
    public async Task OrdinaryClipClickStillFocusesTheTimeline()
    {
        await using var context = new MainWindowTestContext();
        var (timeline, cue, _) = await PrepareAsync(context, 4);
        context.Session.UpdatePreferences(current => current with { TimelineClassicTimingEnabled = false });

        ClickTimeline(context.Window, timeline, cue.Id, new(9), MouseButton.Left);

        Assert.Same(timeline, context.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(cue, context.Session.DocumentSnapshot.Subtitles.Single(line => line.Id == cue.Id));
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(timeline.HasActiveDrag);
    }

    [AvaloniaTheory]
    [InlineData(MouseButton.Left)]
    [InlineData(MouseButton.Right)]
    public async Task ClassicClickFromToolbarFocusStillFocusesTheTimeline(MouseButton button)
    {
        await using var context = new MainWindowTestContext();
        var (timeline, cue, _) = await PrepareAsync(context, 4);
        var toggle = UiTestActions.Find<ToolbarToggleButton>(context.Window,
            "TimelineClassicTimingButton");
        Assert.True(toggle.Focus());
        Flush(context.Window);
        var target = new MediaTime(button == MouseButton.Left ? 7137 : 11137, 1000);

        ClickTimeline(context.Window, timeline, cue.Id, target, button);

        Assert.Same(timeline, context.Window.FocusManager!.GetFocusedElement());
        Assert.True(timeline.IsFocused);
        Assert.Null(context.Session.LastError);
    }

    private static async Task<(SubtitleTimelineControl Timeline, SubtitleLine Cue, TextBox Input)> PrepareAsync(
        MainWindowTestContext context, int column)
    {
        await context.OpenMediaAsync();
        var cue = new SubtitleLine { Start = new(8), End = new(10), Text = "字幕 ABC 123" };
        context.Session.Editor.Reset(context.Session.DocumentSnapshot with
        {
            Subtitles = [cue], Layers = [new()
            {
                Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
            }]
        });
        context.Session.UpdatePreferences(current => current with
        {
            TimelineClassicTimingEnabled = true, TimelineSnapEnabled = false, TimelineStepEnabled = false
        });
        context.ViewModel.Timeline.PixelsPerSecond = 120;
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        Flush(context.Window);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == cue.Id);
        list.ScrollIntoView(row);
        Flush(context.Window);
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(box =>
            box.DataContext is SubtitleRow value && value.Id == cue.Id && Grid.GetColumn(box) == column);
        input.BringIntoView();
        Flush(context.Window);
        var point = input.TranslatePoint(new(input.Bounds.Width / 2, input.Bounds.Height / 2), context.Window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(context.Window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, input) || hit.GetVisualAncestors().Contains(input));
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Flush(context.Window);
        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(cue.Id, context.Session.SelectedCueId);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.IsClassicTimingEnabled);
        Assert.False(context.Session.Editor.CanUndo);
        return (timeline, cue, input);
    }

    private static void ClickTimeline(Window window, SubtitleTimelineControl timeline, Guid cueId, MediaTime time,
        MouseButton button)
    {
        var seconds = (double)time.Numerator / time.Denominator;
        var local = new Point(timeline.HeaderWidth + (seconds - timeline.ViewStart) * timeline.PixelsPerSecond,
            timeline.GetClipRectangle(cueId)!.Value.Center.Y);
        var point = timeline.TranslatePoint(local, window)!.Value;
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, button);
        window.MouseUp(point, button);
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
