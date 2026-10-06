using AegiNext.Core.Projects;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleTrackEditingUiTests
{
    [AvaloniaFact]
    public async Task AddingAnEmptyTrackFiltersRowsAndNewCuesKeepTheirOwnEffectsAndSelection()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        var first = Assert.Single(window.DocumentSnapshot.Subtitles);
        context.Session.Editor.SetKeyframe(first.Id, AnimationProperty.OPACITY, new(new(0), 0.25));
        var originalLayer = Assert.Single(window.DocumentSnapshot.Layers);
        window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        var menu = TimelineTrackTestActions.OpenMenu(context, first.TrackId);
        Assert.True(menu.IsOpen);
        TimelineTrackTestActions.Execute(TimelineTrackTestActions.Item(menu, "AddSubtitleTrackMenuItem"));
        menu.Close();
        var secondTrack = context.Session.CurrentTrackId;

        Assert.NotEqual(first.TrackId, secondTrack);
        Assert.Empty(window.ViewModel.Subtitles.VisibleRows);
        Assert.Single(window.ViewModel.Subtitles.Rows);
        Assert.Null(context.Session.SelectedCue);
        Assert.Null(context.Session.SelectedLayer);

        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        var second = window.DocumentSnapshot.Subtitles[1];
        Assert.Equal(secondTrack, second.TrackId);
        Assert.Equal(second.Id, Assert.Single(window.ViewModel.Subtitles.VisibleRows).Id);
        Assert.Same(originalLayer, window.DocumentSnapshot.Layers[0]);
        Assert.Empty(window.DocumentSnapshot.Layers[1].Tracks);

        var tracks = UiTestActions.Find<ComboBox>(window, "SubtitleTrackCombo");
        tracks.SelectedItem = window.DocumentSnapshot.SubtitleTracks.Single(track => track.Id == first.TrackId);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(first.TrackId, context.Session.CurrentTrackId);
        Assert.Equal(first.Id, Assert.Single(window.ViewModel.Subtitles.VisibleRows).Id);
        Assert.Null(context.Session.SelectedCue);
        Assert.Null(context.Session.SelectedLayer);
        Assert.Equal(first.TrackId, context.ViewModel.Timeline.SelectedTrackId);
        Assert.Equal(2, window.ViewModel.Subtitles.Rows.Length);
    }

    [AvaloniaFact]
    public async Task InvalidCueDraftRejectsTrackSwitchAndPreservesTheVisibleDraft()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.ADD_SUBTITLE).Execute(null);
        var otherTrack = context.Session.Editor.AddSubtitleTrack("Other");
        var row = Assert.Single(window.ViewModel.Subtitles.VisibleRows);
        var original = row.StartText;
        var snapshot = window.DocumentSnapshot;
        try
        {
            row.StartText = "invalid";
            context.Session.SelectTrack(otherTrack);

            Assert.Equal(SubtitleTrack.DEFAULT_TRACK_ID, context.Session.CurrentTrackId);
            Assert.Same(snapshot, window.DocumentSnapshot);
            Assert.Same(row, Assert.Single(window.ViewModel.Subtitles.VisibleRows));
            Assert.Equal("invalid", row.StartText);
            Assert.Equal(row.Id, window.ViewModel.Subtitles.InvalidRowId);
            Assert.Equal("subtitles", window.ViewModel.InvalidPanelId);
        }
        finally
        {
            row.StartText = original;
            Assert.True(window.ViewModel.TryCommitDrafts());
        }
    }
}
