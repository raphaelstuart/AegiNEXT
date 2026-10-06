using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Timeline;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTrackManagementUiTests
{
    [AvaloniaFact]
    public async Task SelectingPopulatedTrackClearsClipAndKeyAndKeyboardCreatesOnSelectedEmptyTrack()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var firstTrack = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var cueId = context.Session.Editor.AddSubtitle(new(0), new(2), "First", firstTrack);
        context.Session.SelectCue(cueId);
        var layer = context.Session.SelectedLayer!;
        context.Session.Editor.SetKeyframe(layer.Id, AnimationProperty.OPACITY, new(new(1), 0.25));
        var originalAnimation = context.Session.SelectedLayer!.Tracks;
        Assert.True(context.Session.SelectKeyframe(new(layer.Id, AnimationProperty.OPACITY, new(1), new(1))));
        var otherTrack = context.Session.Editor.AddSubtitleTrack("Keyboard destination");
        var menu = TimelineTrackTestActions.OpenMenu(context, firstTrack);
        Assert.True(menu.IsOpen);
        Assert.Null(context.Session.SelectedCue);
        Assert.Null(context.Session.SelectedLayer);
        Assert.Null(context.Session.SelectedKeyTime);
        Assert.Empty(context.ViewModel.Effects.SelectedIds);
        Assert.Equal(firstTrack, context.ViewModel.Timeline.SelectedTrackId);
        menu.Close();

        menu = TimelineTrackTestActions.OpenMenu(context, otherTrack);
        Assert.True(menu.IsOpen);
        Assert.Equal(otherTrack, context.Session.CurrentTrackId);
        menu.Close();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.Enter, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        var created = Assert.Single(context.Session.DocumentSnapshot.Subtitles, cue => cue.Id != cueId);
        Assert.Equal(otherTrack, created.TrackId);
        Assert.Equal(created.Id, context.Session.SelectedCue!.Id);
        Assert.Equal(originalAnimation, context.Session.DocumentSnapshot.Layers.Single(value => value.Id == layer.Id).Tracks);
    }

    [AvaloniaFact]
    public async Task RightClickRenameUsesTimelineDraftCancelPreservesAndSaveIsUndoable()
    {
        await using var context = new MainWindowTestContext();
        var trackId = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var original = context.Session.DocumentSnapshot;
        var menu = TimelineTrackTestActions.OpenMenu(context, trackId);
        Assert.True(menu.IsOpen);
        TimelineTrackTestActions.Execute(TimelineTrackTestActions.Item(menu, "RenameSubtitleTrackMenuItem"));
        menu.Close();
        Assert.True(context.ViewModel.Timeline.IsRenamingTrack);
        var input = UiTestActions.Find<TextBox>(context.Window, "TimelineTrackNameInput");
        Assert.True(input.Focus());
        input.SelectAll();
        context.Window.KeyTextInput("Draft only");
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.False(context.ViewModel.Timeline.IsRenamingTrack);
        Assert.Same(original, context.Session.DocumentSnapshot);

        menu = TimelineTrackTestActions.OpenMenu(context, trackId);
        TimelineTrackTestActions.Execute(TimelineTrackTestActions.Item(menu, "RenameSubtitleTrackMenuItem"));
        menu.Close();
        Assert.True(input.Focus());
        input.SelectAll();
        context.Window.KeyTextInput("Dialogue");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Dialogue", context.ViewModel.Timeline.TrackNameDraft);
        UiTestActions.Click(context.Window, "ConfirmTrackRenameButton");
        Assert.False(context.ViewModel.Timeline.IsRenamingTrack);
        Assert.Equal("Dialogue", Assert.Single(context.Session.DocumentSnapshot.SubtitleTracks).Name);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
    }
}
