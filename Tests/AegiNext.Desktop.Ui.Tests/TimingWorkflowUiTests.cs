using AegiNext.Core.Timing;
using AegiNext.Core.Editing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimingWorkflowUiTests
{
    [AvaloniaTheory]
    [InlineData(Key.Q, RawInputModifiers.None)]
    [InlineData(Key.W, RawInputModifiers.None)]
    [InlineData(Key.E, RawInputModifiers.None)]
    [InlineData(Key.R, RawInputModifiers.None)]
    [InlineData(Key.Enter, RawInputModifiers.None)]
    [InlineData(Key.Enter, RawInputModifiers.Shift)]
    public async Task FocusOnlyBindingsInvalidateTimingEvenWhenNoPanelCanHandleThem(Key key, RawInputModifiers modifiers)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        var button = UiTestActions.Find<Button>(window, "AddCueButton");
        Assert.True(button.Focus());
        await context.Controller.PlayAsync();
        UiTestActions.Press(window, Key.F8);
        button = UiTestActions.Find<Button>(window, "AddCueButton");
        Assert.True(button.Focus());
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        Assert.Same(button, window.FocusManager!.GetFocusedElement());
        context.Clock.Advance(TimeSpan.FromSeconds(1));

        UiTestActions.Press(window, key, modifiers);

        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        Assert.False(context.Controller.IsRangePlaybackActive);
        var beforeExit = window.DocumentSnapshot;
        UiTestActions.Press(window, Key.F9);
        Assert.Same(beforeExit, window.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task EnterAlwaysCreatesNewCueAndExitOnlyClosesThatCue()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        context.Session.Editor.SetSubtitleTiming(Assert.Single(window.DocumentSnapshot.Subtitles).Id,
            new(10), new(12), TimelineEditMode.CROP);
        var original = Assert.Single(window.DocumentSnapshot.Subtitles);
        Assert.Null(window.FindControl<Button>("SetStartButton"));
        Assert.Null(window.FindControl<Button>("SetEndButton"));
        UiTestActions.Press(window, Key.F9);
        Assert.Equal(original, Assert.Single(window.DocumentSnapshot.Subtitles));

        await context.Controller.SeekAsync(new(1, 3));
        await context.Controller.PlayAsync();
        UiTestActions.Press(window, Key.F8);
        var active = window.DocumentSnapshot.Subtitles.Single(line => line.Id != original.Id);
        Assert.Equal(new MediaTime(1, 3), active.Start);
        UiTestActions.Press(window, Key.F8);
        Assert.Equal(2, window.DocumentSnapshot.Subtitles.Length);
        Assert.Equal(original, window.DocumentSnapshot.Subtitles.Single(line => line.Id == original.Id));
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        UiTestActions.Press(window, Key.F9);
        var ended = window.DocumentSnapshot.Subtitles.Single(line => line.Id == active.Id);
        Assert.Equal(new MediaTime(4, 3), ended.End);
        Assert.Equal(original, window.DocumentSnapshot.Subtitles.Single(line => line.Id == original.Id));
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        UiTestActions.Press(window, Key.F9);
        Assert.Equal(2, window.DocumentSnapshot.Subtitles.Length);
        UiTestActions.Press(window, Key.F8);
        Assert.Equal(3, window.DocumentSnapshot.Subtitles.Length);
    }

    [AvaloniaTheory]
    [InlineData("command")]
    [InlineData("selection")]
    [InlineData("text")]
    [InlineData("seek")]
    [InlineData("key")]
    [InlineData("wheel")]
    public async Task AnyOtherUserOperationInvalidatesPendingExit(string operation)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        context.Session.Editor.SetSubtitleTiming(Assert.Single(window.DocumentSnapshot.Subtitles).Id,
            new(10), new(12), TimelineEditMode.CROP);
        var original = Assert.Single(window.DocumentSnapshot.Subtitles);
        await context.Controller.PlayAsync();
        UiTestActions.Press(window, Key.F8);
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        var provisional = window.DocumentSnapshot.Subtitles.Single(line => line.Id != original.Id);
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        var list = UiTestActions.Find<ListBox>(window, "SubtitleList");
        window.UpdateLayout();
        switch (operation)
        {
            case "command":
                window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
                break;
            case "selection":
                var item = list.ContainerFromItem(list.Items.OfType<SubtitleRow>().Single(row => row.Id == original.Id))!;
                var point = item.TranslatePoint(new Point(6, 6), window)!.Value;
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                break;
            case "text":
                var text = list.GetVisualDescendants().OfType<TextBox>().First(input => input.AcceptsReturn);
                Assert.True(text.Focus());
                window.KeyTextInput("a");
                break;
            case "seek":
                UiTestActions.Find<Slider>(window, "PositionSlider").Value = 3;
                break;
            case "key":
                UiTestActions.Press(window, Key.Tab);
                break;
            case "wheel":
                window.MouseWheel(list.TranslatePoint(new Point(6, 6), window)!.Value, new Vector(0, -1));
                break;
        }

        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        var beforeExit = window.DocumentSnapshot;
        UiTestActions.Press(window, Key.F9);
        Assert.Same(beforeExit, window.DocumentSnapshot);
        Assert.Equal(original.Start, window.DocumentSnapshot.Subtitles.Single(line => line.Id == original.Id).Start);
        Assert.Equal(original.End, window.DocumentSnapshot.Subtitles.Single(line => line.Id == original.Id).End);
        var frozenEnd = window.DocumentSnapshot.Subtitles.Single(line => line.Id == provisional.Id).End;
        await context.Controller.SeekAsync(frozenEnd);
        UiTestActions.Press(window, Key.F8);
        var focused = window.FocusManager?.GetFocusedElement() as Control;
        Assert.True(window.DocumentSnapshot.Subtitles.Length == 3,
            $"F8 did not create a subtitle. Focus: {focused}, menu open: {focused?.GetVisualAncestors().OfType<MenuBase>().Any(menu => menu.IsOpen)}, submenu open: {(focused as MenuItem)?.IsSubMenuOpen}, position: {context.Session.ProjectPosition}, state: {context.Controller.Snapshot.State}, error: {context.Session.LastError}.");
        Assert.Equal(frozenEnd, window.DocumentSnapshot.Subtitles[^1].Start);
    }
}
