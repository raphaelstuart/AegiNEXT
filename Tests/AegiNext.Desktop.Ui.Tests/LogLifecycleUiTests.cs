using System.Runtime.ExceptionServices;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Workspace.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class LogLifecycleUiTests
{
    [AvaloniaFact]
    public async Task ActiveClipboardFailureRecordsCompleteExceptionAfterActualCopyClick()
    {
        using var journal = new WorkbenchLogJournal();
        using var model = new LogPanelViewModel(journal);
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        string? copied = null;
        using var view = new LogPanelView(model, journal, writeClipboard: text =>
        {
            copied = text;
            return clipboard.Task;
        });
        var window = new Window { Width = 1100, Height = 620, Content = view };
        try
        {
            window.Show();
            journal.Append(WorkbenchLogLevel.INFO, "Project", "Opened");
            var expected = model.CopyVisibleText();
            ClickCopy(window, view);
            Assert.Equal(expected, copied);
            Assert.False(view.CopyCompletion.IsCompleted);
            var error = new IOException("Clipboard write failed", new InvalidOperationException("Complete inner cause"));
            clipboard.SetException(error);
            await view.CopyCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            var failure = Assert.Single(journal.Entries, entry => entry.Source == "Clipboard");
            Assert.Equal(WorkbenchLogLevel.ERROR, failure.Level);
            Assert.Equal(error.ToString(), failure.Details);
        }
        finally
        {
            clipboard.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClipboardFailureAfterOwnerShutdownCannotWriteToReleasedJournal(bool disposePanel)
    {
        using var journal = new WorkbenchLogJournal();
        using var model = new LogPanelViewModel(journal);
        var clipboard = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closing = false;
        using var view = new LogPanelView(model, journal, () => closing, _ => clipboard.Task);
        var window = new Window { Width = 1100, Height = 620, Content = view };
        try
        {
            window.Show();
            journal.Append(WorkbenchLogLevel.INFO, "Project", "Opened");
            ClickCopy(window, view);
            Assert.False(view.CopyCompletion.IsCompleted);
            window.Close();
            if (disposePanel)
            {
                view.Dispose();
                Assert.False(view.IsEnabled);
            }
            else
            {
                closing = true;
            }
            model.Dispose();
            journal.Dispose();
            clipboard.SetException(new IOException("Clipboard completed after shutdown"));
            await view.CopyCompletion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(WorkbenchLogLevel.INFO, Assert.Single(journal.Entries).Level);
        }
        finally
        {
            clipboard.TrySetResult();
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DirectMainDisposalDrainsPendingLogsWithoutUpdatingReleasedLayout()
    {
        await using var context = new MainWindowTestContext();
        var main = context.Window;
        await main.Session.Styles.Completion;
        main.Session.Journal.Clear();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var layoutReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failures = new List<Exception>();
        var host = Assert.IsType<DockControl>(main.Layouts.Host);
        EventHandler<AvaloniaPropertyChangedEventArgs> captureLayoutRelease = (_, e) =>
        {
            if (e.Property == DockControl.LayoutProperty && host.Layout is null)
            {
                layoutReleased.TrySetResult();
            }
        };
        EventHandler<FirstChanceExceptionEventArgs> captureDisposedLayout = (_, e) =>
        {
            if (e.Exception is ObjectDisposedException error &&
                error.ObjectName?.Contains(nameof(WorkbenchLayoutController), StringComparison.Ordinal) == true)
            {
                failures.Add(error);
            }
        };
        host.PropertyChanged += captureLayoutRelease;
        AppDomain.CurrentDomain.FirstChanceException += captureDisposedLayout;
        Task? disposal = null;
        try
        {
            main.Session.Styles.Queue(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                main.Session.LogInfo("Styles", "Pending operation drained during shutdown");
            });
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            disposal = main.DisposeAsync().AsTask();
            Assert.False(main.FindControl<ContentControl>("WorkspaceHost")!.IsEnabled);
            await layoutReleased.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(disposal.IsCompleted);
            Assert.True(main.Session.IsClosing);
            release.SetResult();
            await disposal.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(main.Session.Journal.Entries, entry => entry.Message == "Pending operation drained during shutdown");
            Assert.Empty(failures);
            Assert.Empty(main.WindowRegistry.Windows);
        }
        finally
        {
            release.TrySetResult();
            try
            {
                if (disposal is not null)
                {
                    await disposal.WaitAsync(TimeSpan.FromSeconds(5));
                }
            }
            finally
            {
                host.PropertyChanged -= captureLayoutRelease;
                AppDomain.CurrentDomain.FirstChanceException -= captureDisposedLayout;
            }
        }
    }

    private static void ClickCopy(Window window, LogPanelView view)
    {
        var button = view.FindControl<Button>("CopyLogVisible")!;
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window);
        Assert.NotNull(point);
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point.Value));
        Assert.True(ReferenceEquals(hit, button) || hit.GetVisualAncestors().Contains(button));
        window.MouseDown(point.Value, MouseButton.Left);
        window.MouseUp(point.Value, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }
}
