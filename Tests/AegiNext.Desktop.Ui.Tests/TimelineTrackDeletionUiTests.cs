using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineTrackDeletionUiTests
{
    [AvaloniaTheory]
    [InlineData("DeleteButton", true)]
    [InlineData("CancelButton", false)]
    [InlineData("Escape", false)]
    [InlineData("Close", false)]
    public async Task PopulatedTrackMenuUsesRealConfirmationAndOneUndo(string action, bool deleted)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var trackId = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var cue = context.Session.Editor.AddSubtitle(new(0), new(2), "删除 中文 ABC 123", trackId);
        context.Session.Editor.SetKeyframe(cue, AnimationProperty.OPACITY, new(new(1), 0.4));
        var menu = TimelineTrackTestActions.OpenMenu(context, trackId);
        var before = context.Session.DocumentSnapshot;
        var item = TimelineTrackTestActions.Item(menu, "DeleteSubtitleTrackMenuItem");
        Assert.Equal("Delete track", item.Header);
        TimelineTrackTestActions.Execute(item);
        menu.Close();
        var operation = context.ViewModel.Timeline.DeleteTrackCommand.ExecutionTask!;
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<TrackDeletionDialog>());
        try
        {
            Assert.True(context.Session.IsProjectBusy);
            Assert.Contains("business-surface", dialog.Classes);
            Assert.True(UiTestActions.Find<Button>(dialog, "CancelButton").IsDefault);
            Assert.False(UiTestActions.Find<Button>(dialog, "DeleteButton").IsDefault);
            Assert.Contains(ProjectTrack.Default.Name, UiTestActions.Find<TextBlock>(dialog, "TrackDeletionMessage").Text);
            if (action == "Escape")
            {
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            }
            else if (action == "Close")
            {
                dialog.Close();
            }
            else
            {
                UiTestActions.Click(dialog, action);
            }
            await operation;
            Dispatcher.UIThread.RunJobs();
            Assert.False(dialog.IsVisible);
            Assert.False(context.Session.IsProjectBusy);
            Assert.Null(context.Session.LastError);
            if (!deleted)
            {
                Assert.Same(before, context.Session.DocumentSnapshot);
                return;
            }
            Assert.Empty(context.Session.DocumentSnapshot.Tracks);
            Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
            Assert.Empty(context.Session.DocumentSnapshot.Layers);
            Assert.Null(context.ViewModel.Subtitles.SelectedTrack);
            Assert.Null(context.ViewModel.Timeline.SelectedTrackId);
            Assert.Null(context.Session.SelectedCue);
            Assert.Null(context.Session.SelectedLayer);
            Assert.Null(context.Session.SelectedKeyTime);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(before, context.Session.DocumentSnapshot);
            Assert.True(context.Session.Editor.Redo());
            Assert.Empty(context.Session.DocumentSnapshot.Tracks);
        }
        finally
        {
            dialog.Close(false);
            await operation;
        }
    }

    [AvaloniaFact]
    public async Task EmptyLastTrackDeletesWithoutDialogAndKeyboardCreationWaitsForANewTrack()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var trackId = Assert.IsType<Guid>(context.Session.CurrentTrackId);
        var menu = TimelineTrackTestActions.OpenMenu(context, trackId);
        TimelineTrackTestActions.Execute(TimelineTrackTestActions.Item(menu, "DeleteSubtitleTrackMenuItem"));
        menu.Close();
        await context.ViewModel.Timeline.DeleteTrackCommand.ExecutionTask!;
        Assert.Empty(context.Window.OwnedWindows.OfType<TrackDeletionDialog>());
        Assert.Empty(context.Session.DocumentSnapshot.Tracks);
        var before = context.Session.DocumentSnapshot;
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.F8);
        UiTestActions.Press(context.Window, Key.Enter, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(before, context.Session.DocumentSnapshot);
        Assert.Null(context.Session.LastError);

        await context.ViewModel.Timeline.AddTrackCommand.ExecuteAsync(null);
        Assert.Single(context.Session.DocumentSnapshot.Tracks);
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.F8);
        Dispatcher.UIThread.RunJobs();
        var timing = Assert.IsAssignableFrom<IAsyncRelayCommand>(context.Window.GetCommand(WorkbenchCommand.TIMING_ENTER));
        Assert.NotNull(timing.ExecutionTask);
        await timing.ExecutionTask.WaitAsync(TimeSpan.FromSeconds(5));
        await context.Session.WaitForProjectIdleAsync();
        Assert.Equal(context.Session.CurrentTrackId, context.Session.ClipIndex.GetSubtitleTrackId(Assert.Single(context.Session.DocumentSnapshot.Subtitles).Id));
        Assert.Null(context.Session.LastError);
    }

    [AvaloniaTheory]
    [InlineData("en-US", WorkbenchTheme.LIGHT)]
    [InlineData("zh-CN", WorkbenchTheme.DARK)]
    public async Task ConfirmationFitsLongMixedTrackNameAndReleasesLiveTranslations(string language, WorkbenchTheme theme)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(value => value with { Theme = theme });
        Localization.SetLanguage(language);
        var service = new WindowWorkbenchDialogService(context.Window, registerWindow: context.WindowRegistry.RegisterAuxiliary);
        var name = "字幕 ABC 123 " + new string('x', 110);
        var answer = service.ConfirmTrackDeletionAsync(name, 123, TestContext.Current.CancellationToken);
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<TrackDeletionDialog>());
        try
        {
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            var message = UiTestActions.Find<TextBlock>(dialog, "TrackDeletionMessage");
            var cancel = UiTestActions.Find<Button>(dialog, "CancelButton");
            var delete = UiTestActions.Find<Button>(dialog, "DeleteButton");
            Assert.Equal(Localization.Format("Workbench.DeleteTrackConfirmation", name, 123), message.Text);
            Assert.Equal(Localization.Get("Workbench.DeleteTrack"), dialog.Title);
            Assert.Contains("PingFang SC", message.FontFamily.Name, StringComparison.Ordinal);
            Assert.Equal(20, message.LineHeight);
            Assert.Equal(SizeToContent.Height, dialog.SizeToContent);
            var bottom = delete.TranslatePoint(new(0, delete.Bounds.Height), dialog)!.Value.Y;
            Assert.InRange(dialog.ClientSize.Height - bottom, 19, 21);
            Assert.True(message.Bounds.Width <= dialog.ClientSize.Width - 40);
            Localization.SetLanguage(language == "en-US" ? "zh-CN" : "en-US");
            Assert.Equal(Localization.Format("Workbench.DeleteTrackConfirmation", name, 123), message.Text);
            Assert.Equal(Localization.Get("Workbench.Cancel"), cancel.Content);
            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.False(await answer);
            var title = dialog.Title;
            var text = message.Text;
            Localization.SetLanguage(language);
            Assert.Equal(title, dialog.Title);
            Assert.Equal(text, message.Text);
            Assert.Empty(context.Window.OwnedWindows.OfType<TrackDeletionDialog>());
        }
        finally
        {
            dialog.Close(false);
            await answer;
        }
    }

    [AvaloniaFact]
    public async Task CancellingPendingConfirmationClosesTheWindow()
    {
        await using var context = new MainWindowTestContext();
        using var cancellation = new CancellationTokenSource();
        var service = new WindowWorkbenchDialogService(context.Window, registerWindow: context.WindowRegistry.RegisterAuxiliary);
        var answer = service.ConfirmTrackDeletionAsync("Cancel", 1, cancellation.Token);
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<TrackDeletionDialog>());
        try
        {
            cancellation.Cancel();
            Dispatcher.UIThread.RunJobs();
            Assert.False(await answer);
            Assert.False(dialog.IsVisible);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ConfirmTrackDeletionAsync("Cancel", 1, cancellation.Token));
            Assert.Empty(context.Window.OwnedWindows.OfType<TrackDeletionDialog>());
        }
        finally
        {
            dialog.Close(false);
            await answer;
        }
    }
}
