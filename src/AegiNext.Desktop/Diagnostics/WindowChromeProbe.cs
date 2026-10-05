using System.Text.Json;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class WindowChromeProbe : IDisposable
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private readonly WindowChromeProbeOptions options;
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly WindowChromeProbeReport report = new();
    private readonly List<WindowChromeProbeWindow> windows = [];
    private readonly CancellationTokenSource lifetime = new();
    private readonly Size originalSize = new(940, 620);
    private bool windowMenu;

    internal WindowChromeProbe(WindowChromeProbeOptions options, IClassicDesktopStyleApplicationLifetime desktop)
    {
        this.options = options;
        this.desktop = desktop;
        windowMenu = !OperatingSystem.IsMacOS();
        MainWindow = new("Main", originalSize, ToggleMenu, RestoreSize, ShowChildren);
        windows.Add(MainWindow);
        MainWindow.Opened += OnOpened;
        MainWindow.Closed += OnClosed;
    }

    internal WindowChromeProbeWindow MainWindow { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        lifetime.Dispose();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        try
        {
            ShowChildren();
            Capture("opened");
            if (!options.Automatic)
            {
                return;
            }

            await SettleAsync();
            Capture("settled");
            for (var i = 0; i < 3; i++)
            {
                MainWindow.ResizeClient(new(originalSize.Width + 120, originalSize.Height + 80));
                await SettleAsync();
                MainWindow.ResizeClient(originalSize);
                await SettleAsync();
                Capture($"restore-{i + 1}");
                VerifySize(originalSize);
            }

            ToggleMenu();
            await SettleAsync();
            Capture("menu-toggled");
            ToggleMenu();
            await SettleAsync();
            Capture("menu-restored");
            MainWindow.WindowState = WindowState.Maximized;
            await SettleAsync();
            Capture("maximized");
            MainWindow.WindowState = WindowState.Normal;
            await SettleAsync();
            MainWindow.ResizeClient(originalSize);
            await SettleAsync();
            Capture("normal-restored");
            VerifySize(originalSize);
            await VerifyAutoHeightDialogAsync(false);
            await VerifyAutoHeightDialogAsync(true);
            MainWindow.RejectNextClose = true;
            MainWindow.Close();
            if (!MainWindow.IsVisible)
            {
                report.Failures.Add("Cancelled close destroyed the probe window.");
            }

            Capture("close-cancelled");
            report.AutomaticChecksCompleted = true;
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            report.Failures.Add(error.ToString());
        }
        finally
        {
            if ((options.Automatic || report.Failures.Count > 0) && MainWindow.IsVisible)
            {
                MainWindow.RejectNextClose = false;
                MainWindow.Close();
            }
        }
    }

    private async Task SettleAsync()
    {
        await Task.Delay(150, lifetime.Token);
        await Dispatcher.UIThread.InvokeAsync(() => MainWindow.UpdateLayout(), DispatcherPriority.Render, lifetime.Token);
    }

    private void VerifySize(Size expected)
    {
        if (Math.Abs(MainWindow.ClientSize.Width - expected.Width) > 1 ||
            Math.Abs(MainWindow.ClientSize.Height - expected.Height) > 1)
        {
            report.Failures.Add($"Client size drift: expected {expected}, actual {MainWindow.ClientSize}.");
        }
    }

    private async Task VerifyAutoHeightDialogAsync(bool wrapped)
    {
        var dialog = new UnsavedProjectDialog();
        var body = (StackPanel)dialog.Content!;
        var message = (TextBlock)body.Children[0];
        var originalMessage = message.Text;
        var wrappedMessage = string.Join(" ", Enumerable.Repeat("The project contains unsaved changes. Please choose whether to save them before continuing.", 4));
        if (wrapped)
        {
            message.Text = wrappedMessage;
        }
        var titleBar = new WindowTitleBar();
        var root = new DockPanel();
        DockPanel.SetDock(titleBar, Avalonia.Controls.Dock.Top);
        root.Children.Add(titleBar);
        dialog.Content = null;
        root.Children.Add(body);
        dialog.Content = root;
        using var chrome = WindowChrome.Attach(dialog, titleBar);
        Task<int>? completion = null;
        try
        {
            completion = dialog.ShowDialog<int>(MainWindow);
            await SettleAsync();
            dialog.UpdateLayout();
            CaptureAutoHeightDialog(dialog, root, body, chrome, wrapped ? "wrapped" : "short");
            if (!wrapped)
            {
                message.Text = wrappedMessage;
                await SettleAsync();
                dialog.UpdateLayout();
                CaptureAutoHeightDialog(dialog, root, body, chrome, "grown");
                message.Text = originalMessage;
                await SettleAsync();
                dialog.UpdateLayout();
                CaptureAutoHeightDialog(dialog, root, body, chrome, "shrunk");
            }
        }
        finally
        {
            dialog.Close(0);
            if (completion is not null)
            {
                await completion;
            }
        }
    }

    private void CaptureAutoHeightDialog(Window dialog, Control root, StackPanel body, IWindowChrome chrome, string scenario)
    {
        var button = dialog.GetVisualDescendants().OfType<Button>().Single(control => control.Name == "SaveButton");
        var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), dialog)!.Value.Y;
        var gap = dialog.ClientSize.Height - bottom;
        var failure = OperatingSystem.IsWindows() && chrome is WindowsWindowChrome native ? native.LastError?.ToString() : null;
        report.AutoHeightDialogs.Add(new(scenario, dialog.SizeToContent.ToString(),
            dialog.ClientSize.Height, root.DesiredSize.Height, gap, failure));
        if (dialog.SizeToContent != SizeToContent.Height || Math.Abs(dialog.ClientSize.Height - root.DesiredSize.Height) > 2 ||
            Math.Abs(gap - body.Margin.Bottom) > 2 || failure is not null)
        {
            report.Failures.Add($"Auto-height dialog did not fit its contents ({scenario}): client={dialog.ClientSize.Height}, desired={root.DesiredSize.Height}, bottom gap={gap}, native={failure}.");
        }
    }

    private void ShowChildren()
    {
        foreach (var (host, size, offset) in new[]
                 {
                     ("Floating", new Size(580, 380), new PixelPoint(80, 90)),
                     ("Settings", new Size(680, 480), new PixelPoint(180, 180))
                 })
        {
            if (windows.FirstOrDefault(window => window.Host == host && window.IsVisible) is { } existing)
            {
                existing.Activate();
                continue;
            }

            var child = new WindowChromeProbeWindow(host, size, ToggleMenu, RestoreSize, ShowChildren);
            child.SetWindowMenu(windowMenu);
            child.Position = MainWindow.Position + offset;
            windows.Add(child);
            child.Show(MainWindow);
        }
    }

    private void ToggleMenu()
    {
        windowMenu = !windowMenu;
        foreach (var window in windows.Where(window => window.IsVisible))
        {
            window.SetWindowMenu(windowMenu);
        }

        Capture("menu-changed");
    }

    private void RestoreSize()
    {
        MainWindow.ResizeClient(originalSize);
        Capture("restore-requested");
    }

    private void Capture(string action)
    {
        foreach (var sample in windows.Where(window => window.IsVisible).Select(window => window.Capture(action)))
        {
            report.Samples.Add(sample);
            if (sample.PlatformFailure is { } failure)
            {
                report.Failures.Add($"{sample.Host}: {failure}");
            }
            if (sample.VisibleMacOsSystemButtons is { } count && count != 3 && sample.WindowState != "FullScreen")
            {
                report.Failures.Add($"{sample.Host}: expected three visible NSWindow buttons, observed {count}.");
            }

            if (action == "settled" && sample.NativeCaptionButtonsMeasured is false)
            {
                report.Failures.Add($"{sample.Host}: native caption button bounds were not available after settling.");
            }
            if (action == "settled" && sample.NativeNonClientRenderingEnabled is false)
            {
                report.Failures.Add($"{sample.Host}: DWM native non-client rendering was disabled.");
            }
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        lifetime.Cancel();
        foreach (var window in windows.Where(window => window.IsVisible).ToArray())
        {
            window.RejectNextClose = false;
            window.Close();
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath)!);
            File.WriteAllBytes(options.ReportPath, JsonSerializer.SerializeToUtf8Bytes(report, jsonOptions));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            report.Failures.Add(error.ToString());
        }

        MainWindow.Opened -= OnOpened;
        MainWindow.Closed -= OnClosed;
        Dispose();
        desktop.Shutdown(report.Failures.Count == 0 ? 0 : 1);
    }
}
