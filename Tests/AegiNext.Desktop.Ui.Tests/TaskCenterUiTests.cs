using AegiNext.Application.Tasks;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Tasks;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TaskCenterUiTests
{
    [AvaloniaFact]
    public async Task FailedHistoryRemainsTwoRowsAndExposesItsProgressAndFailureToAccessibility()
    {
        using var environment = new UiTestEnvironment();
        await using var service = new AegiTaskService();
        var task = new TaskCenterTestTask("Tasks.OpenProject", failure: new IOException("Corrupt project input"));
        var handle = service.Submit(task);
        await task.Started.Task;
        task.Context!.ReportProgress(new("Tasks.OpenProject", 3, 10));
        task.Finish.TrySetResult();
        task.Cleanup.TrySetResult();
        await Assert.ThrowsAsync<IOException>(() => handle.Completion);
        using var model = new TaskCenterViewModel(service);
        var view = new TaskCenterView { DataContext = model };
        var window = new Window { Width = 300, Height = 360, Content = new WindowTitleBar { RightContent = view } };
        try
        {
            window.Show();
            var button = UiTestActions.Find<Button>(view, "TasksButton");
            Assert.True(button.Focus());
            UiTestActions.Press(window, Key.Enter);
            var content = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(button.Flyout).Content);
            FlushPopup(window, content);
            var presenter = Assert.Single(content.GetVisualAncestors().OfType<FlyoutPresenter>());
            SavePopupReview(presenter, "en-US", false, 300, "task-center-failed");
            var card = Assert.Single(presenter.GetVisualDescendants().OfType<Border>(), value => value.Name == "TaskCard");
            var row = Assert.Single(model.RecentTasks);
            Assert.Equal(2, UiTestActions.Find<Grid>(card, "TaskCardLayout").RowDefinitions.Count);
            Assert.False(UiTestActions.Find<Button>(card, "TaskCancelButton").IsVisible);
            Assert.Equal(Localization.Get("Tasks.Failed"), UiTestActions.Find<TextBlock>(card, "TaskSecondaryText").Text);
            var progress = UiTestActions.Find<ProgressBar>(card, "TaskProgressBar");
            Assert.False(progress.IsIndeterminate);
            Assert.Equal(30, progress.Value);
            var peer = ControlAutomationPeer.CreatePeerForElement(progress);
            Assert.Contains(row.ProgressText, peer.GetHelpText(), StringComparison.Ordinal);
            Assert.Contains("Corrupt project input", peer.GetHelpText(), StringComparison.Ordinal);
            Assert.Contains("Corrupt project input", Assert.IsType<string>(ToolTip.GetTip(card)), StringComparison.Ordinal);
        }
        finally
        {
            view.CloseFlyout();
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaTheory]
    [InlineData(300, "en-US", false)]
    [InlineData(300, "en-US", true)]
    [InlineData(300, "zh-CN", false)]
    [InlineData(300, "zh-CN", true)]
    [InlineData(760, "en-US", false)]
    [InlineData(760, "en-US", true)]
    [InlineData(760, "zh-CN", false)]
    [InlineData(760, "zh-CN", true)]
    public async Task TaskCardsFitNarrowAndWidePopupsWithSharedTypographyAndStaticHistory(double width, string language, bool dark)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 2 };
        var longName = Localization.Get("Tasks.TimingPostProcessor") + " · " +
            string.Concat(Enumerable.Repeat("字幕 Subtitle 123 ", 8));
        var running = new TaskCenterTestTask(longName);
        var cancelling = new TaskCenterTestTask("Tasks.TimingPostProcessor");
        var queued = new TaskCenterTestTask("Tasks.SettingsImport");
        var success = new TaskCenterTestTask("Tasks.SaveProject");
        var cancelled = new TaskCenterTestTask("Tasks.VideoExport");
        var tasks = new[] { running, cancelling, queued, success, cancelled };
        var handles = new List<AegiTaskHandle>();
        TaskCenterView? view = null;
        Window? window = null;
        try
        {
            handles.Add(service.Submit(running));
            await running.Started.Task;
            running.Context!.ReportProgress(new("Tasks.TimingCalculation", 2, 5));
            var successHandle = service.Submit(success);
            handles.Add(successHandle);
            await success.Started.Task;
            success.Finish.TrySetResult();
            success.Cleanup.TrySetResult();
            await successHandle.Completion;
            var cancelledHandle = service.Submit(cancelled);
            handles.Add(cancelledHandle);
            await cancelled.Started.Task;
            cancelled.Context!.ReportProgress(new("Tasks.Encoding", 2, 5));
            cancelled.Cleanup.TrySetResult();
            cancelledHandle.RequestCancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledHandle.Completion);
            var cancellingHandle = service.Submit(cancelling);
            handles.Add(cancellingHandle);
            await cancelling.Started.Task;
            cancelling.Context!.ReportProgress(new("Tasks.TimingIndex"));
            cancellingHandle.RequestCancel();
            handles.Add(service.Submit(queued));

            using var model = new TaskCenterViewModel(service);
            view = new() { DataContext = model };
            window = new()
            {
                Width = width,
                Height = 620,
                FontSize = 24,
                FontFamily = new("monospace"),
                RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
                Content = new WindowTitleBar { RightContent = view }
            };
            window.Show();
            var button = UiTestActions.Find<Button>(view, "TasksButton");
            Assert.True(button.Focus());
            UiTestActions.Press(window, Key.Enter);
            var content = Assert.IsAssignableFrom<Control>(Assert.IsType<Flyout>(button.Flyout).Content);
            FlushPopup(window, content);
            var presenter = Assert.Single(content.GetVisualAncestors().OfType<FlyoutPresenter>());
            SavePopupReview(presenter, language, dark, width);
            AssertPopupHasNoHorizontalOverflow(window, presenter);

            var expectedFontSize = (double)view.FindResource("WorkbenchBodyFontSize")!;
            var expectedFontFamily = (FontFamily)view.FindResource("WorkbenchBodyFontFamily")!;
            var expectedLineHeight = (double)view.FindResource("WorkbenchInputLineHeight")!;
            var iconSize = (double)view.FindResource("WorkbenchControlHeight")!;
            var gap = (double)view.FindResource("WorkbenchGapSmall")!;
            var cards = presenter.GetVisualDescendants().OfType<Border>().Where(card => card.Name == "TaskCard").ToArray();
            Assert.Equal(5, cards.Length);
            foreach (var card in cards)
            {
                Assert.Contains("card", card.Classes);
                var layout = UiTestActions.Find<Grid>(card, "TaskCardLayout");
                Assert.Equal(2, layout.RowDefinitions.Count);
                var name = UiTestActions.Find<TextBlock>(card, "TaskNameText");
                var secondary = UiTestActions.Find<TextBlock>(card, "TaskSecondaryText");
                foreach (var text in new[] { name, secondary })
                {
                    Assert.Equal(expectedFontSize, text.FontSize);
                    Assert.Equal(expectedFontFamily, text.FontFamily);
                    Assert.Equal(expectedLineHeight, text.LineHeight);
                    Assert.Equal(TextTrimming.CharacterEllipsis, text.TextTrimming);
                    var point = text.TranslatePoint(default, card)!.Value;
                    Assert.InRange(point.X, 0, card.Bounds.Width);
                    Assert.True(point.X + text.Bounds.Width <= card.Bounds.Width + 0.5);
                }

                Assert.True(secondary.Bounds.Width > 0);
                Assert.Equal(name.Bounds.Right + gap, secondary.Bounds.Left, precision: 4);
                var row = Assert.IsType<TaskRowViewModel>(card.DataContext);
                Assert.Equal(row.SecondaryTitle, secondary.Text);
                var progress = UiTestActions.Find<ProgressBar>(card, "TaskProgressBar");
                Assert.Equal(1, Grid.GetRow(progress));
                Assert.True(progress.Bounds.Height > 0);
                var cancel = UiTestActions.Find<Button>(card, "TaskCancelButton");
                if (cancel.IsVisible)
                {
                    Assert.Equal(iconSize, cancel.Bounds.Width);
                    Assert.Equal(cancel.Bounds.Width, cancel.Bounds.Height);
                    Assert.Equal(row.CanCancel, cancel.IsEffectivelyEnabled);
                }

                if (row.Id == successHandle.Id)
                {
                    Assert.False(progress.IsIndeterminate);
                    Assert.Equal(100, progress.Value);
                    Assert.False(cancel.IsVisible);
                }
                else if (row.Id == cancelledHandle.Id)
                {
                    Assert.False(progress.IsIndeterminate);
                    Assert.Equal(40, progress.Value);
                    Assert.False(cancel.IsVisible);
                }
                else if (row.Id == cancellingHandle.Id)
                {
                    Assert.Equal(Localization.Get("Tasks.Cancelling"), secondary.Text);
                    Assert.True(cancel.IsVisible);
                    Assert.False(cancel.IsEffectivelyEnabled);
                }
            }

            var historyRows = UiTestActions.Find<ItemsControl>(content, "RecentTaskRows");
            var historyCards = historyRows.GetVisualDescendants().OfType<Border>().Where(card => card.Name == "TaskCard").ToArray();
            var firstPoint = historyCards[0].TranslatePoint(default, historyRows)!.Value;
            var secondPoint = historyCards[1].TranslatePoint(default, historyRows)!.Value;
            Assert.Equal(firstPoint.Y + historyCards[0].Bounds.Height + gap, secondPoint.Y, precision: 4);
            var clear = UiTestActions.Find<Button>(content, "ClearTaskHistoryButton");
            Assert.Equal(iconSize, clear.Bounds.Width);
            Assert.Equal(clear.Bounds.Width, clear.Bounds.Height);
            Assert.Equal(Localization.Get("Tasks.ClearHistory"), AutomationProperties.GetName(clear));
            Assert.Equal(Localization.Get("Tasks.ClearHistory"), ToolTip.GetTip(clear));
            Assert.DoesNotContain(presenter.GetVisualDescendants().OfType<TextBlock>(),
                text => text.Text == Localization.Get("Tasks.Application"));

            window.Width = width < 480 ? 760 : 300;
            FlushPopup(window, content);
            AssertPopupHasNoHorizontalOverflow(window, presenter);
            Assert.True(clear.Focus());
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(model.RecentTasks);
            Assert.Equal(3, model.ActiveCount);
        }
        finally
        {
            foreach (var task in tasks)
            {
                task.Finish.TrySetResult();
                task.Cleanup.TrySetResult();
            }

            foreach (var handle in handles)
            {
                handle.RequestCancel();
            }

            await service.DrainAsync();
            view?.CloseFlyout();
            window?.Close();
            if (window is not null)
            {
                Assert.False(window.IsVisible);
            }
        }
    }

    [AvaloniaFact]
    public async Task ProgressRefreshKeepsTheExistingRowKeyboardFocusAndScrollOffset()
    {
        using var environment = new UiTestEnvironment();
        await using var service = new AegiTaskService { MaximumConcurrentTasks = 1 };
        using var model = new TaskCenterViewModel(service);
        var tasks = Enumerable.Range(0, 16).Select(index => new TaskCenterTestTask("Scan " + index)).ToArray();
        var handles = tasks.Select(task => service.Submit(task)).ToArray();
        var view = new TaskCenterView { DataContext = model };
        var window = new Window { Width = 760, Height = 620, Content = new WindowTitleBar { RightContent = view } };
        try
        {
            await tasks[0].Started.Task;
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var button = UiTestActions.Find<Button>(view, "TasksButton");
            Assert.True(button.Focus());
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            var flyout = Assert.IsType<Flyout>(button.Flyout);
            var content = Assert.IsAssignableFrom<Control>(flyout.Content);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var items = UiTestActions.Find<ItemsControl>(content, "CurrentTaskRows");
            var cancel = items.GetVisualDescendants().OfType<Button>().First();
            Assert.True(cancel.Focus(),
                $"Cancel focus: focused={cancel.IsFocused}, visible={cancel.IsEffectivelyVisible}, enabled={cancel.IsEffectivelyEnabled}, bounds={cancel.Bounds}.");
            var scroll = UiTestActions.Find<ScrollViewer>(content, "TaskListScrollViewer");
            scroll.Offset = new Vector(0, 80);
            var offset = scroll.Offset;
            Assert.True(offset.Y > 0);
            var row = model.CurrentTasks[0];
            var progressUpdated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            row.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(TaskRowViewModel.ProgressValue) && row.ProgressValue == 75)
                {
                    progressUpdated.TrySetResult();
                }
            };
            tasks[0].Context!.ReportProgress(new("Tasks.TimingCalculation", 3, 4));
            await progressUpdated.Task.WaitAsync(TimeSpan.FromSeconds(3));
            Dispatcher.UIThread.RunJobs();
            Assert.Same(row, model.CurrentTasks[0]);
            Assert.Same(cancel, items.GetVisualDescendants().OfType<Button>().First());
            Assert.True(cancel.IsFocused);
            Assert.Equal(offset, scroll.Offset);
        }
        finally
        {
            foreach (var task in tasks)
            {
                task.Finish.TrySetResult();
                task.Cleanup.TrySetResult();
            }

            foreach (var handle in handles)
            {
                handle.RequestCancel();
            }

            await service.DrainAsync();
            view.CloseFlyout();
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public async Task CancelKeepsStableRowUntilCleanupAndHistoryClearPreservesOtherRunningTasks()
    {
        using var environment = new UiTestEnvironment();
        await using var service = new AegiTaskService();
        using var model = new TaskCenterViewModel(service);
        var cancellable = new TaskCenterTestTask("Tasks.TimingPostProcessor");
        var other = new TaskCenterTestTask("Tasks.VideoExport", false);
        var first = service.Submit(cancellable);
        var second = service.Submit(other);
        try
        {
            await Task.WhenAll(cancellable.Started.Task, other.Started.Task);
            Dispatcher.UIThread.RunJobs();
            var row = model.CurrentTasks[0];
            Assert.Equal(first.Id, row.Id);
            Assert.Equal(second.Id, model.CurrentTasks[1].Id);
            Assert.False(model.CurrentTasks[1].ShowsCancel);
            row.CancelCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(row, model.CurrentTasks[0]);
            Assert.Equal(Localization.Get("Tasks.Cancelling"), row.Status);
            Assert.True(row.ShowsCancel);
            Assert.False(row.CanCancel);
            Assert.Equal(2, model.ActiveCount);

            cancellable.Cleanup.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.Completion);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(row, Assert.Single(model.RecentTasks));
            Assert.False(row.ShowsCancel);
            Assert.Equal(1, model.ActiveCount);
            model.ClearHistoryCommand.Execute(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(model.RecentTasks);
            Assert.Equal(second.Id, Assert.Single(model.CurrentTasks).Id);
        }
        finally
        {
            cancellable.Finish.TrySetResult();
            cancellable.Cleanup.TrySetResult();
            other.Finish.TrySetResult();
            other.Cleanup.TrySetResult();
            try
            {
                await first.Completion;
            }
            catch (OperationCanceledException)
            {
            }

            await second.Completion;
        }
    }

    [AvaloniaFact]
    public async Task TitleBarTaskButtonOpensStandardFlyoutWithKeyboardAndUpdatesLanguage()
    {
        using var environment = new UiTestEnvironment();
        await using var service = new AegiTaskService();
        using var model = new TaskCenterViewModel(service);
        var view = new TaskCenterView { DataContext = model };
        var titleBar = new WindowTitleBar { RightContent = view };
        var window = new Window { Width = 760, Height = 300, Content = titleBar };
        try
        {
            window.Show();
            window.UpdateLayout();
            var button = UiTestActions.Find<Button>(view, "TasksButton");
            Assert.True(button.Focus());
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.Flyout!.IsOpen);
            Assert.Equal("Tasks (0 active)", model.ButtonName);
            Localization.SetLanguage("zh-CN");
            Assert.Equal("任务（0 个未结束）", model.ButtonName);
            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();
            Assert.False(button.Flyout.IsOpen);
            Assert.True(button.IsFocused);
        }
        finally
        {
            view.CloseFlyout();
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    private static void FlushPopup(Window window, Control content)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        TopLevel.GetTopLevel(content)!.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static void AssertPopupHasNoHorizontalOverflow(Window window, FlyoutPresenter presenter)
    {
        Assert.True(presenter.Bounds.Width <= window.ClientSize.Width);
        var content = UiTestActions.Find<Border>(presenter, "TaskFlyoutContent");
        var contentOrigin = content.TranslatePoint(default, presenter)!.Value;
        Assert.True(contentOrigin.X >= presenter.Padding.Left);
        Assert.True(contentOrigin.X + content.Bounds.Width <= presenter.Bounds.Width - presenter.Padding.Right);
        var scrolls = presenter.GetVisualDescendants().OfType<ScrollViewer>().ToArray();
        Assert.True(scrolls.Length >= 2);
        foreach (var scroll in scrolls)
        {
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width + 0.5,
                $"{scroll.Name ?? "Flyout template"}: horizontal extent {scroll.Extent.Width} exceeds viewport {scroll.Viewport.Width}.");
            Assert.Equal(0, scroll.Offset.X);
        }
    }

    private static void SavePopupReview(FlyoutPresenter presenter, string language, bool dark, double width, string prefix = "task-center")
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext.TaskCenter.Review");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{prefix}-{language}-{(dark ? "dark" : "light")}-{width}.png");
        using var target = new RenderTargetBitmap(new((int)Math.Ceiling(presenter.Bounds.Width),
            (int)Math.Ceiling(presenter.Bounds.Height)), new(96, 96));
        target.Render(presenter);
        using var output = File.Create(path);
        target.Save(output, PngBitmapEncoderOptions.Default);
    }
}
