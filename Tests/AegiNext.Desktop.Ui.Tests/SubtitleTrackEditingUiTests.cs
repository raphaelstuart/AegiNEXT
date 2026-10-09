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
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        var window = context.Window;
        await window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var first = Assert.Single(window.DocumentSnapshot.Subtitles);
        var firstTrack = context.Session.ClipIndex.GetSubtitleTrackId(first.Id);
        context.Session.Editor.SetKeyframe(first.Id, AnimationProperty.OPACITY, new(new(0), 0.25));
        var originalLayer = Assert.Single(window.DocumentSnapshot.Layers);
        window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        var menu = TimelineTrackTestActions.OpenMenu(context, firstTrack);
        Assert.True(menu.IsOpen);
        TimelineTrackTestActions.Execute(TimelineTrackTestActions.Item(menu, "AddSubtitleTrackMenuItem"));
        menu.Close();
        var secondTrack = context.Session.CurrentTrackId;

        Assert.NotEqual(firstTrack, secondTrack);
        Assert.Empty(window.ViewModel.Subtitles.VisibleRows);
        Assert.Single(window.ViewModel.Subtitles.Rows);
        Assert.Null(context.Session.SelectedCue);
        Assert.Null(context.Session.SelectedLayer);

        await window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var second = window.DocumentSnapshot.Subtitles[1];
        Assert.Equal(secondTrack, context.Session.ClipIndex.GetSubtitleTrackId(second.Id));
        Assert.Equal(second.Id, Assert.Single(window.ViewModel.Subtitles.VisibleRows).Id);
        Assert.Same(originalLayer, window.DocumentSnapshot.Layers[0]);
        Assert.Empty(window.DocumentSnapshot.Layers[1].Tracks);

        var tracks = UiTestActions.Find<ComboBox>(window, "SubtitleTrackCombo");
        tracks.SelectedItem = window.DocumentSnapshot.Tracks.Single(track => track.Id == firstTrack);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(firstTrack, context.Session.CurrentTrackId);
        Assert.Equal(first.Id, Assert.Single(window.ViewModel.Subtitles.VisibleRows).Id);
        Assert.Null(context.Session.SelectedCue);
        Assert.Null(context.Session.SelectedLayer);
        Assert.Equal(firstTrack, context.ViewModel.Timeline.SelectedTrackId);
        Assert.Equal(2, window.ViewModel.Subtitles.Rows.Length);
    }

    [AvaloniaFact]
    public async Task InvalidCueDraftRejectsTrackSwitchAndPreservesTheVisibleDraft()
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        await context.OpenMediaAsync();
        var window = context.Window;
        await window.ViewModel.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var otherTrack = context.Session.Editor.AddTrack("Other");
        var row = Assert.Single(window.ViewModel.Subtitles.VisibleRows);
        var original = row.StartText;
        var snapshot = window.DocumentSnapshot;
        try
        {
            row.StartText = "invalid";
            context.Session.SelectTrack(otherTrack);

            Assert.Equal(ProjectTrack.DEFAULT_TRACK_ID, context.Session.CurrentTrackId);
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
