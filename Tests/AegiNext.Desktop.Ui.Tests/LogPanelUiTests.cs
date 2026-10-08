using AegiNext.Desktop.I18n;
using System.Globalization;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class LogPanelUiTests
{
    [AvaloniaFact]
    public async Task InactiveStandardLogDoesNotReadErrorsOrChangeTheActiveSubtitleTab()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.Styles.Completion;
        var view = main.Panels[WorkbenchPanelIds.LOG];
        var owner = Assert.IsAssignableFrom<Dock.Model.Core.IDock>(main.Layouts.PanelAdapters[WorkbenchPanelIds.LOG].Owner);
        var subtitles = main.Layouts.PanelAdapters[WorkbenchPanelIds.SUBTITLES];
        Assert.True(main.Layouts.IsVisible(WorkbenchPanelIds.LOG));
        Assert.Same(subtitles, owner.ActiveDockable);
        Assert.False(view.IsAttachedToVisualTree());
        main.Session.Journal.Clear();
        main.Session.LogError("Project", new IOException("Unread while its tab is inactive"));
        Flush(main);
        Assert.False(view.IsAttachedToVisualTree());
        Assert.Same(subtitles, owner.ActiveDockable);
        Assert.Equal(1, main.ViewModel.Log.UnreadErrorCount);

        await main.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_LOG);
        Flush(main);
        Assert.Same(main.Layouts.PanelAdapters[WorkbenchPanelIds.LOG], owner.ActiveDockable);
        Assert.True(view.IsAttachedToVisualTree() && view.IsEffectivelyVisible);
        Assert.True(view.Bounds.Width > 0 && view.Bounds.Height > 0);
        Assert.Equal(0, main.ViewModel.Log.UnreadErrorCount);
    }

    [AvaloniaFact]
    public async Task HiddenLogKeepsHistoryUnreadBadgeAndOneViewAcrossDocking()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.Styles.Completion;
        var view = main.Panels[WorkbenchPanelIds.LOG];
        main.Layouts.Hide(WorkbenchPanelIds.LOG);
        main.Session.Journal.Clear();
        main.Session.LogInfo("Project", "Opened");
        main.Session.LogWarning("Analysis", "No audio track");
        var error = new InvalidOperationException("Export failed", new IOException("Full nested cause"));
        main.Session.LogError("Export", error);
        Flush(main);

        Assert.Null(main.FindControl<Border>("ErrorPanel"));
        Assert.Equal(3, main.Session.Journal.Entries.Count);
        Assert.Equal(1, main.ViewModel.Log.UnreadErrorCount);
        Assert.EndsWith("(1)", main.Layouts.PanelAdapters[WorkbenchPanelIds.LOG].Title);
        using var editLease = main.Session.AcquireEditingLease();
        try
        {
            Flush(main);
            Assert.All(main.Panels.Where(pair => pair.Key != WorkbenchPanelIds.LOG), pair => Assert.False(pair.Value.IsEnabled));
            Assert.True(view.IsEnabled);
            var tasksButton = UiTestActions.Find<Button>(main, "TasksButton");
            Assert.True(tasksButton.IsEffectivelyEnabled);
            Assert.True(tasksButton.Focus());
            UiTestActions.Press(main, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(tasksButton.Flyout!.IsOpen);
            UiTestActions.Press(main, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.False(tasksButton.Flyout.IsOpen);
            Assert.True(tasksButton.IsFocused);
            Assert.True(main.GetCommand(WorkbenchCommand.VIEW_LOG).CanExecute(null));
            await main.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_LOG);
            Flush(main);
            Assert.True(view.IsAttachedToVisualTree());
            Assert.True(view.IsEffectivelyVisible);
            Assert.Equal(0, main.ViewModel.Log.UnreadErrorCount);
            Assert.Same(main.ViewModel.Log, view.DataContext);
        }
        finally
        {
            editLease.Dispose();
        }

        main.Layouts.Float(WorkbenchPanelIds.LOG);
        Flush(main);
        var floating = Assert.Single(main.Layouts.FloatingWindows);
        floating.Close();
        Flush(main);
        main.Session.LogError("Export", new IOException("Second failure"));
        Assert.Equal(1, main.ViewModel.Log.UnreadErrorCount);
        main.Layouts.Show(WorkbenchPanelIds.LOG);
        Flush(main);
        Assert.Same(view, main.Panels[WorkbenchPanelIds.LOG]);
        Assert.Same(view, main.Layouts.PanelAdapters[WorkbenchPanelIds.LOG].View);
        Assert.Contains(main.Session.Journal.Entries, entry => entry.Details == error.ToString());
        Assert.Equal(0, main.ViewModel.Log.UnreadErrorCount);
    }

    [AvaloniaFact]
    public async Task ActualPanelFiltersCopiesFullDetailsAndClearsThroughControls()
    {
        using var environment = new UiTestEnvironment();
        using var journal = new WorkbenchLogJournal();
        using var model = new LogPanelViewModel(journal);
        using var view = new LogPanelView(model, journal);
        var window = new Window { Width = 1100, Height = 620, Content = view };
        try
        {
            window.Show();
            journal.Append(WorkbenchLogLevel.INFO, "Project", "Opened");
            journal.Append(WorkbenchLogLevel.WARNING, "Analysis", "Warning");
            var error = new InvalidOperationException("Failure", new IOException("Needle complete detail"));
            journal.ReportError("Export", error);
            Flush(window);
            var levels = view.FindControl<ComboBox>("LogLevelFilter")!;
            levels.SelectedIndex = 3;
            Flush(window);
            Assert.Equal(3, model.FilterIndex);
            Assert.Single(model.Entries);

            var search = view.FindControl<TextBox>("LogSearch")!;
            Click(window, search);
            search.SelectAll();
            window.KeyTextInput("Needle");
            Flush(window);
            Assert.Equal("Needle", model.FilterText);
            Assert.Single(model.Entries);
            var list = view.FindControl<ListBox>("LogEntries")!;
            list.SelectedIndex = 0;
            Flush(window);
            Assert.Null(view.FindControl<TextBox>("LogDetails"));
            var details = Assert.Single(list.GetVisualDescendants().OfType<Expander>(), control => control.IsVisible);
            Click(window, details);
            Flush(window);
            Assert.True(details.IsExpanded);
            var detailText = Assert.Single(details.GetVisualDescendants().OfType<SelectableTextBlock>());
            Assert.Equal(error.ToString(), detailText.Text);
            Click(window, view.FindControl<Button>("CopyLogSelected")!);
            Dispatcher.UIThread.RunJobs();
            var clipboard = Assert.IsAssignableFrom<IClipboard>(window.Clipboard);
            Assert.Contains(error.ToString(), await clipboard.TryGetTextAsync());
            Localization.SetLanguage("zh-CN");
            model.RefreshLanguage();
            Flush(window);
            Assert.Equal(3, levels.SelectedIndex);
            Assert.Equal("错误", levels.SelectedItem);
            Assert.Equal("Needle", search.Text);
            Click(window, view.FindControl<Button>("ClearLog")!);
            Flush(window);
            Assert.Empty(journal.Entries);
            Assert.Empty(model.Entries);
            Assert.Null(model.SelectedEntry);
            Assert.Equal(string.Empty, model.Details);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, Control control)
    {
        control.BringIntoView();
        Flush(window);
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Assert.NotNull(window.CaptureRenderedFrame());
        var point = control.TranslatePoint(new(control.Bounds.Width / 2, control.Bounds.Height / 2), window);
        Assert.NotNull(point);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point.Value));
        Assert.True(ReferenceEquals(hit, control) || hit.GetVisualAncestors().Contains(control));
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
