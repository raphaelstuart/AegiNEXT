using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.LogicalTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTrackStylesUiTests
{
    [AvaloniaFact]
    public async Task TimingShortcutCreatesSubtitleWithSelectedTrackDefaultStyle()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await context.Session.Styles.Completion;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Timing", new() { FontSize = 43, Italic = true });
        await context.Session.Styles.UpsertAsync(preset);
        var trackId = context.Session.Editor.AddTrack("Timing track");
        await context.Session.ApplySubtitleTrackStyleAsync(trackId, preset.Id);
        Assert.True(context.Session.SelectTrack(trackId));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.Focus());

        UiTestActions.Press(context.Window, Key.F8);
        Dispatcher.UIThread.RunJobs();
        var timing = Assert.IsAssignableFrom<IAsyncRelayCommand>(context.Window.GetCommand(WorkbenchCommand.TIMING_ENTER));
        Assert.NotNull(timing.ExecutionTask);
        await timing.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Session.WaitForProjectIdleAsync();

        var cue = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(trackId, context.Session.ClipIndex.GetSubtitleTrackId(cue.Id));
        Assert.Equal(preset.Style, cue.Style);
        Assert.Equal(cue.Id, context.Session.SelectedCue?.Id);
        Assert.True(context.Session.Editor.Undo());
        Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(preset.Style, context.Session.DocumentSnapshot.Tracks.Single(track => track.Id == trackId).DefaultStyle);
    }

    [AvaloniaFact]
    public async Task TrackContextPresetsApplyStableIdsBadgeSelectsTrackAndNoGlobalStyleEntryRemains()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await context.Session.Styles.Completion;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Dialogue", new() { FontSize = 47, Bold = true });
        await context.Session.Styles.UpsertAsync(preset);
        var emptyTrack = context.Session.Editor.AddTrack("Empty");
        var original = context.Session.DocumentSnapshot;
        var menu = TimelineTrackTestActions.OpenMenu(context, emptyTrack);
        var trackStyles = TimelineTrackTestActions.Item(menu, "SubtitleTrackStyleMenuItem");
        var choice = Assert.Single(trackStyles.Items.OfType<MenuItem>());
        Assert.Equal(preset.Name, choice.Header);
        Assert.True(choice.Command!.CanExecute(choice.CommandParameter));
        choice.Command.Execute(choice.CommandParameter);
        await ((IAsyncRelayCommand)choice.Command).ExecutionTask!;
        menu.Close();
        Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(preset.Style, context.Session.DocumentSnapshot.Tracks.Single(track => track.Id == emptyTrack).DefaultStyle);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.True(context.Session.Editor.Redo());

        Assert.True(context.Session.SelectTrack(emptyTrack));
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.Enter, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        var create = Assert.IsAssignableFrom<IAsyncRelayCommand>(context.Window.GetCommand(WorkbenchCommand.ADD_SUBTITLE));
        Assert.NotNull(create.ExecutionTask);
        await create.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Session.WaitForProjectIdleAsync();
        var created = Assert.Single(context.Session.DocumentSnapshot.Subtitles);
        Assert.Equal(emptyTrack, context.Session.ClipIndex.GetSubtitleTrackId(created.Id));
        Assert.Equal(preset.Style, created.Style);
        var badge = timeline.GetTrackStyleBadgeRectangle(emptyTrack)!.Value;
        Assert.True(timeline.GetTrackHeaderRectangle(emptyTrack)!.Value.Contains(badge));
        var point = timeline.TranslatePoint(badge.Center, context.Window)!.Value;
        context.Window.MouseDown(point, MouseButton.Left);
        context.Window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(emptyTrack, context.Session.CurrentTrackId);
        Assert.Null(context.Session.SelectedCue);
        menu = TimelineTrackTestActions.OpenMenu(context, emptyTrack);
        Assert.DoesNotContain(menu.Items.OfType<MenuItem>(), item => item.Name == "AllSubtitleTracksStyleMenuItem");
        var automatic = TimelineTrackTestActions.Item(menu, "AutoApplySubtitleTrackStyleMenuItem");
        Assert.True(automatic.IsChecked);
        automatic.Command!.Execute(automatic.CommandParameter);
        await ((IAsyncRelayCommand)automatic.Command).ExecutionTask!;
        menu.Close();
        var track = context.Session.DocumentSnapshot.Tracks.Single(value => value.Id == emptyTrack);
        Assert.False(track.AutoApplyStyle);
        Assert.Equal(preset.Style, track.DefaultStyle);
        Assert.Equal(created.Style, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style);
    }

    [AvaloniaTheory]
    [InlineData("NoButton", false, false)]
    [InlineData("YesButton", true, false)]
    [InlineData("CancelButton", false, true)]
    public async Task ChangingTrackPresetUsesRealDecisionDialogAndOneUndo(string button, bool updateExisting, bool cancelled)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.Styles.Completion;
        var preset = new SubtitleStylePreset(Guid.NewGuid(), "Replacement", new() { FontSize = 75 });
        await context.Session.Styles.UpsertAsync(preset);
        var trackId = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var cue = context.Session.Editor.AddSubtitle(new(0), new(2), "existing", trackId);
        var otherTrack = context.Session.Editor.AddTrack("Other");
        var otherCue = context.Session.Editor.AddSubtitle(new(0), new(2), "Other", otherTrack);
        context.Session.Editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.4));
        var before = context.Session.DocumentSnapshot;
        var operation = context.Session.ApplySubtitleTrackStyleAsync(trackId, preset.Id);
        Dispatcher.UIThread.RunJobs();
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<TrackStyleChangeDialog>());
        var noButton = dialog.GetLogicalDescendants().OfType<Button>().Single(value => value.Name == "NoButton");
        Assert.True(noButton.IsDefault);
        Assert.Equal(SizeToContent.Height, dialog.SizeToContent);
        var target = dialog.GetLogicalDescendants().OfType<Button>().Single(value => value.Name == button);
        dialog.UpdateLayout();
        var targetPoint = target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), dialog)!.Value;
        dialog.MouseDown(targetPoint, MouseButton.Left);
        dialog.MouseUp(targetPoint, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        await operation;
        Assert.False(dialog.IsVisible);
        Assert.False(context.Session.IsProjectBusy);
        if (cancelled)
        {
            Assert.Same(before, context.Session.DocumentSnapshot);
            return;
        }

        Assert.Equal(preset.Style, context.Session.DocumentSnapshot.Tracks.Single(value => value.Id == trackId).DefaultStyle);
        Assert.Equal(updateExisting ? preset.Style : before.Subtitles.Single(value => value.Id == cue).Style,
            context.Session.DocumentSnapshot.Subtitles.Single(value => value.Id == cue).Style);
        Assert.Same(before.Subtitles.Single(value => value.Id == otherCue),
            context.Session.DocumentSnapshot.Subtitles.Single(value => value.Id == otherCue));
        Assert.Equal(before.Layers, context.Session.DocumentSnapshot.Layers);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task DisabledAutomaticTrackStyleUsesCurrentPanelPresetAndThenBaseWhenLibraryIsEmpty()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await context.Session.Styles.Completion;
        var trackPreset = new SubtitleStylePreset(Guid.NewGuid(), "Track default", new() { FontSize = 48 });
        var currentPreset = new SubtitleStylePreset(Guid.NewGuid(), "Panel choice", new() { FontSize = 83 });
        await context.Session.Styles.UpsertAsync(trackPreset);
        await context.Session.ApplySubtitleTrackStyleAsync(Assert.IsType<Guid>(context.Session.CurrentTrackId), trackPreset.Id);
        await context.Session.Styles.UpsertAsync(currentPreset);
        await context.Session.ToggleTrackAutoApplyStyleAsync(Assert.IsType<Guid>(context.Session.CurrentTrackId));
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        Assert.Equal(currentPreset.Style, Assert.Single(context.Session.DocumentSnapshot.Subtitles).Style);
        Assert.Equal(trackPreset.Style, context.Session.DocumentSnapshot.Tracks[0].DefaultStyle);
        await context.Session.Styles.DeleteAsync(currentPreset.Id);
        await context.Session.Styles.DeleteAsync(trackPreset.Id);
        await context.Session.SeekProjectTimeAsync(new(3));
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        Assert.Equal(new SubtitleStyle(), context.Session.DocumentSnapshot.Subtitles.Single(line => line.Start == new AegiNext.Core.Timing.MediaTime(3)).Style);
    }
}
