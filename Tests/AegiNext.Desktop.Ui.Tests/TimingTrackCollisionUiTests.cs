using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimingTrackCollisionUiTests
{
    [AvaloniaFact]
    public async Task EnterCollisionPreservesTheProjectAndSelectionThenAnEmptyTrackAcceptsTheSameTime()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        var window = context.Window;
        await window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var original = Assert.Single(window.DocumentSnapshot.Subtitles);
        var selectedLayer = context.Session.SelectedLayer;
        await context.Controller.SeekAsync(new(1, 3));
        var before = window.DocumentSnapshot;

        UiTestActions.Press(window, Key.F8);
        await context.Session.WaitForProjectIdleAsync();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(before, window.DocumentSnapshot);
        Assert.Equal(original.Id, context.Session.SelectedCue!.Id);
        Assert.Same(selectedLayer, context.Session.SelectedLayer);
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        Assert.False(string.IsNullOrWhiteSpace(window.ViewModel.Error));
        var alternateTrack = context.Session.Editor.AddTrack("Alternate timing");
        context.Session.SelectTrack(alternateTrack);
        UiTestActions.Press(window, Key.F8);
        await context.Session.WaitForProjectIdleAsync();
        Dispatcher.UIThread.RunJobs();

        var active = window.DocumentSnapshot.Subtitles.Single(line => line.Id != original.Id);
        Assert.Equal(new MediaTime(1, 3), active.Start);
        Assert.Equal(alternateTrack, context.Session.ClipIndex.GetSubtitleTrackId(active.Id));
        Assert.Equal(original, window.DocumentSnapshot.Subtitles.Single(line => line.Id == original.Id));
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        await context.Controller.PlayAsync();
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        UiTestActions.Press(window, Key.F9);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new MediaTime(4, 3), window.DocumentSnapshot.Subtitles.Single(line => line.Id == active.Id).End);
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
    }

    [AvaloniaFact]
    public async Task ExitUsesTheClampedFollowingEndAndCompletesBeforeTheNextCue()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        var window = context.Window;
        context.Session.Editor.AddSubtitle(new(3), new(4), "Following cue", context.Session.CurrentTrackId);
        var following = Assert.Single(window.DocumentSnapshot.Subtitles);
        await context.Controller.PlayAsync();
        UiTestActions.Press(window, Key.F8);
        await context.Session.WaitForProjectIdleAsync();
        var active = window.DocumentSnapshot.Subtitles.Single(line => line.Id != following.Id);
        var before = window.DocumentSnapshot;
        Assert.Equal(MediaTime.Zero, active.Start);
        Assert.Equal(new MediaTime(1, 1000), active.End);
        Assert.True(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        context.Clock.Advance(TimeSpan.FromSeconds(3.5));

        UiTestActions.Press(window, Key.F9);
        Dispatcher.UIThread.RunJobs();

        Assert.NotSame(before, window.DocumentSnapshot);
        Assert.Equal(active.Start, window.DocumentSnapshot.Subtitles.Single(line => line.Id == active.Id).Start);
        Assert.Equal(following.Start, window.DocumentSnapshot.Subtitles.Single(line => line.Id == active.Id).End);
        Assert.Equal(following, window.DocumentSnapshot.Subtitles.Single(line => line.Id == following.Id));
        Assert.Equal(active.Id, context.Session.SelectedCue!.Id);
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
        Assert.Null(window.ViewModel.Error);
        var completed = window.DocumentSnapshot;
        UiTestActions.Press(window, Key.F9);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(completed, window.DocumentSnapshot);
        Assert.False(window.GetCommand(WorkbenchCommand.TIMING_EXIT).CanExecute(null));
    }
}
