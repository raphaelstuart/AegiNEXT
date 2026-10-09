using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleTrackOrderingUiTests
{
    [AvaloniaFact]
    public async Task TimelineContextMenuReordersTracksPreservesCompositionAndSelectedTrackAndUndoEachCommand()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var ids = await PrepareThreeTracksAsync(context);
        var window = context.Window;
        var original = window.DocumentSnapshot;
        var menu = TimelineTrackTestActions.OpenMenu(context, ids[0]);
        Assert.True(menu.IsOpen);
        var selectedIds = Array.Empty<Guid>();
        var up = TimelineTrackTestActions.Item(menu, "MoveSubtitleTrackUpMenuItem");
        var down = TimelineTrackTestActions.Item(menu, "MoveSubtitleTrackDownMenuItem");
        Assert.False(up.IsEffectivelyEnabled);
        Assert.False(up.Command!.CanExecute(null));
        Assert.True(down.IsEffectivelyEnabled);
        Assert.True(down.Command!.CanExecute(null));

        TimelineTrackTestActions.Execute(down);
        var middle = window.DocumentSnapshot;
        AssertTrackState(context, original, [ids[1], ids[0], ids[2]], selectedIds);
        Assert.True(up.IsEffectivelyEnabled);
        Assert.True(down.IsEffectivelyEnabled);

        TimelineTrackTestActions.Execute(down);
        var bottom = window.DocumentSnapshot;
        AssertTrackState(context, original, [ids[1], ids[2], ids[0]], selectedIds);
        Assert.True(up.IsEffectivelyEnabled);
        Assert.False(down.IsEffectivelyEnabled);
        Assert.False(down.Command.CanExecute(null));

        TimelineTrackTestActions.Execute(up);
        AssertTrackState(context, original, [ids[1], ids[0], ids[2]], selectedIds);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(bottom, window.DocumentSnapshot);
        AssertTrackState(context, original, [ids[1], ids[2], ids[0]], selectedIds);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(middle, window.DocumentSnapshot);
        AssertTrackState(context, original, [ids[1], ids[0], ids[2]], selectedIds);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, window.DocumentSnapshot);
        AssertTrackState(context, original, ids, selectedIds);
        Assert.False(up.IsEffectivelyEnabled);
        Assert.True(down.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task ActualReorderButtonRejectsInvalidCueInputAndReturnsFocusWithoutChangingAnyIdentity()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var ids = await PrepareThreeTracksAsync(context);
        var window = context.Window;
        var original = window.DocumentSnapshot;
        var selectedIds = context.ViewModel.Effects.SelectedIds.ToArray();
        var row = Assert.Single(context.ViewModel.Subtitles.VisibleRows);
        var list = UiTestActions.Find<ListBox>(window, "SubtitleList");
        var input = Assert.Single(list.GetVisualDescendants().OfType<TextBox>(), control =>
            ReferenceEquals(control.DataContext, row) && Grid.GetColumn(control) == 1);
        var validText = row.StartText;
        try
        {
            ClickVisibleControl(window, input);
            Assert.True(input.IsFocused);
            input.SelectAll();
            window.KeyTextInput("invalid");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("invalid", row.StartText);
            context.ViewModel.Timeline.MoveTrackDownCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();

            Assert.Same(original, window.DocumentSnapshot);
            AssertTrackState(context, original, ids, selectedIds);
            Assert.Equal("invalid", row.StartText);
            Assert.Equal(row.Id, context.ViewModel.Subtitles.InvalidRowId);
            Assert.Equal("subtitles", context.ViewModel.InvalidPanelId);
            Assert.Equal("StartText", context.ViewModel.InvalidFieldKey);
            Assert.True(input.IsFocused);
        }
        finally
        {
            input.Text = validText;
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.ViewModel.TryCommitDrafts());
        }
    }

    private static async Task<Guid[]> PrepareThreeTracksAsync(MainWindowTestContext context)
    {
        var window = context.Window;
        await context.Session.ExecuteCommandAsync(WorkbenchCommand.ADD_SUBTITLE);
        var first = Assert.Single(window.DocumentSnapshot.Subtitles);
        var second = context.Session.Editor.AddTrack("Second");
        var third = context.Session.Editor.AddTrack("Third");
        context.Session.Editor.AddSubtitle(MediaTime.Zero, new(2), "Second clip", second);
        context.Session.Editor.AddSubtitle(MediaTime.Zero, new(2), "Third clip", third);
        context.Session.Editor.Apply("Track ordering scene fixture", document => document with
        {
            Width = 256, Height = 160,
            Subtitles = document.Subtitles.Select(line => line with
            {
                Text = line.Id == first.Id ? "First clip" : line.Text,
                Style = new() { FontFamily = "sans-serif", FontSize = 22, Margins = new(8, 8, 8) }
            }).ToImmutableArray()
        });
        context.Session.Editor.SetKeyframe(first.Id, AnimationProperty.OPACITY, new(MediaTime.Zero, 0.25));
        var layers = window.DocumentSnapshot.Layers;
        context.Session.SelectLayer(layers[0].Id, [layers[0].Id, layers[1].Id]);
        window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.True(window.ViewModel.TryCommitDrafts());
        Assert.Equal(new ProjectClipIndex(window.DocumentSnapshot).GetSubtitleTrackId(first.Id), context.Session.CurrentTrackId);
        return [new ProjectClipIndex(window.DocumentSnapshot).GetSubtitleTrackId(first.Id), second, third];
    }

    private static void AssertTrackState(MainWindowTestContext context, ProjectDocument original,
        Guid[] expectedOrder, Guid[] selectedIds)
    {
        var document = context.Window.DocumentSnapshot;
        Assert.Equal(expectedOrder, document.Tracks.Select(track => track.Id));
        Assert.Equal(original.Layers, document.Layers);
        Assert.Equal(original.Subtitles, document.Subtitles);
        for (var index = 0; index < original.Layers.Length; index++)
        {
            Assert.Same(original.Layers[index], document.Layers[index]);
            Assert.Same(original.Subtitles[index], document.Subtitles[index]);
        }
        Assert.Equal(new ProjectClipIndex(original).GetSubtitleTrackId(original.Subtitles[0].Id), context.Session.CurrentTrackId);
        if (selectedIds.Length == 0)
        {
            Assert.Null(context.Session.SelectedCue);
            Assert.Null(context.Session.SelectedLayer);
        }
        else
        {
            Assert.Same(original.Subtitles[0], context.Session.SelectedCue);
            Assert.Same(original.Layers[0], context.Session.SelectedLayer);
        }
        Assert.Equal(selectedIds, context.ViewModel.Effects.SelectedIds);
        Assert.Equal(original.Subtitles[0].Id, Assert.Single(context.ViewModel.Subtitles.VisibleRows).Id);
        Assert.Equal(new ProjectClipIndex(original).GetSubtitleTrackId(original.Subtitles[0].Id), Assert.IsType<ProjectTrack>(
            UiTestActions.Find<ComboBox>(context.Window, "SubtitleTrackCombo").SelectedItem).Id);
    }

    private static void ClickVisibleControl(MainWindow window, Control control)
    {
        Assert.True(control.IsEffectivelyEnabled);
        control.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
