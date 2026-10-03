using System.Text.Json;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;

namespace AegiNext.Desktop.Diagnostics;

internal sealed class WorkspaceProbe
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };
    private readonly WorkspaceProbeOptions options;
    private readonly IClassicDesktopStyleApplicationLifetime desktop;
    private readonly WorkspaceProbeReport report = new();

    internal WorkspaceProbe(WorkspaceProbeOptions options, IClassicDesktopStyleApplicationLifetime desktop)
    {
        this.options = options;
        this.desktop = desktop;
        var previous = Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY");
        try
        {
            Environment.SetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY", options.ProfileDirectory);
            MainWindow = new();
        }
        finally
        {
            Environment.SetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY", previous);
        }
        MainWindow.Opened += OnOpened;
    }

    internal MainWindow MainWindow { get; }

    private async void OnOpened(object? sender, EventArgs args)
    {
        try
        {
            await SettleAsync();
            Capture("standard");
            foreach (var id in new[] { WorkbenchPanelIds.PREVIEW, WorkbenchPanelIds.TIMELINE, WorkbenchPanelIds.SUBTITLES, WorkbenchPanelIds.STYLES })
            {
                Verify(MainWindow.Panels[id].IsAttachedToVisualTree(), $"Standard panel attached: {id}");
            }
            var panelInstances = MainWindow.Panels.ToDictionary(pair => pair.Key, pair => pair.Value);
            var controller = MainWindow.Session.Controller;
            foreach (var id in new[] { WorkspaceLayoutPresets.TIMING, WorkspaceLayoutPresets.EFFECTS, WorkspaceLayoutPresets.ENCODE, WorkspaceLayoutPresets.STANDARD })
            {
                Verify(await MainWindow.Layouts.ApplyPresetAsync(id), $"Applied preset: {id}");
                await SettleAsync();
                Capture(id);
            }
            Verify(panelInstances.All(pair => ReferenceEquals(pair.Value, MainWindow.Panels[pair.Key])) &&
                   ReferenceEquals(controller, MainWindow.Session.Controller), "All six views and the media controller retained their identity");
            Verify(!MainWindow.Session.Editor.CanUndo, "Layout changes did not add an undo entry");
            var preset = await MainWindow.Layouts.SaveAsAsync("Runtime verification");
            Verify(preset is not null, "Saved a personal preset");
            MainWindow.Layouts.Hide(WorkbenchPanelIds.EXPORT);
            await MainWindow.Layouts.FlushAsync();
            var stored = new WorkspaceLayoutStore(options.ProfileDirectory).Load();
            Verify(stored.Current.HiddenPanelIds.Contains(WorkbenchPanelIds.EXPORT) &&
                   !stored.Presets.Single().Layout.HiddenPanelIds.Contains(WorkbenchPanelIds.EXPORT), "Automatic layout memory did not overwrite the named preset");
            MainWindow.Layouts.Float(WorkbenchPanelIds.PREVIEW);
            await SettleAsync();
            var floating = MainWindow.Layouts.FloatingWindows.Single();
            Verify(MainWindow.Panels[WorkbenchPanelIds.PREVIEW].IsAttachedToVisualTree(), "Preview view attached to its real floating host");
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
            await SettleAsync();
            Capture("system-menu-three-hosts");
            MainWindow.Session.UpdatePreferences(MainWindow.Session.Preferences with { WindowMenuOnMac = true, Language = "en-US" });
            await SettleAsync();
            Capture("window-menu-three-hosts");
            Verify(MainWindow.WindowRegistry.Windows.All(window => FindTitleBar(window).MenuContent is not null), "Window menus applied to all three hosts");
            MainWindow.Session.UpdatePreferences(MainWindow.Session.Preferences with { WindowMenuOnMac = false, Language = "zh-CN" });
            await SettleAsync();
            Capture("system-menu-restored");
            if (OperatingSystem.IsMacOS())
            {
                Verify(MainWindow.WindowRegistry.Windows.All(window => NativeMenu.GetIsNativeMenuExported(window) && FindTitleBar(window).MenuContent is null), "Native system menus restored on all macOS hosts");
            }
            floating.Close();
            await SettleAsync();
            Verify(MainWindow.IsVisible && !MainWindow.Layouts.IsVisible(WorkbenchPanelIds.PREVIEW), "Closing the floating host only hid its panel");
            await MainWindow.ViewModel.ExecuteCommandAsync(WorkbenchCommand.VIEW_PREVIEW);
            await SettleAsync();
            Verify(MainWindow.Layouts.IsVisible(WorkbenchPanelIds.PREVIEW), "Shared view command reopened the preview panel");
            Verify(await MainWindow.Layouts.RenameAsync(preset!, "Renamed verification") && await MainWindow.Layouts.DeleteAsync(preset!), "Renamed and deleted the personal preset");
            await MainWindow.Layouts.RestoreDefaultAsync();
            await MainWindow.Layouts.FlushAsync();
            Capture("final-standard");
            report.Completed = true;
        }
        catch (Exception error)
        {
            report.Failures.Add(error.ToString());
        }
        finally
        {
            try
            {
                await MainWindow.DisposeAsync();
                MainWindow.Close();
                Verify(!MainWindow.IsVisible && MainWindow.WindowRegistry.Windows.Count == 0, "All registered windows were closed and released");
            }
            catch (Exception error)
            {
                report.Failures.Add(error.ToString());
            }
            Directory.CreateDirectory(Path.GetDirectoryName(options.ReportPath)!);
            await File.WriteAllBytesAsync(options.ReportPath, JsonSerializer.SerializeToUtf8Bytes(report, jsonOptions));
            Directory.Delete(options.ProfileDirectory, true);
            desktop.Shutdown(report.Completed && report.Failures.Count == 0 ? 0 : 1);
        }
    }

    private void Capture(string action)
    {
        var host = (DockControl)MainWindow.Layouts.Host;
        var visuals = host.GetVisualDescendants().OfType<Control>().ToArray();
        report.DockSamples.Add(new(action, host.IsAttachedToVisualTree(), host.IsEffectivelyVisible,
            host.Bounds.ToString(), host.Layout?.GetType().Name, host.Layout?.ActiveDockable?.GetType().Name,
            host.DataContext?.GetType().Name, visuals.OfType<RootDockControl>().Count(),
            visuals.OfType<ToolDockControl>().Count(),
            host.GetVisualAncestors().OfType<Control>().Select(control =>
                $"{control.GetType().Name}/{control.Name}: visible={control.IsVisible}, bounds={control.Bounds}").ToArray(),
            visuals.Take(80).Select(control =>
                $"{control.GetType().Name}/{control.Name}: visible={control.IsVisible}, bounds={control.Bounds}, data={control.DataContext?.GetType().Name}").ToArray(),
            MainWindow.Panels.Select(pair =>
                $"{pair.Key}: attached={pair.Value.IsAttachedToVisualTree()}, visible={pair.Value.IsEffectivelyVisible}, bounds={pair.Value.Bounds}, parent={pair.Value.GetVisualParent()?.GetType().Name}, data={pair.Value.DataContext?.GetType().Name}").ToArray()));
        foreach (var window in MainWindow.WindowRegistry.Windows.Where(window => window.IsVisible))
        {
            var titleBar = FindTitleBar(window);
            int? buttons = null;
            if (OperatingSystem.IsMacOS() && window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSWindow: not 0 } handle)
            {
                buttons = MacOsCaptionButtons.CountVisible(handle.NSWindow);
                Verify(buttons == 3, $"Three native traffic lights visible: {action}/{window.GetType().Name}");
            }
            report.Samples.Add(new(action, window.GetType().Name, window.Title ?? string.Empty, window.ClientSize.Width,
                window.ClientSize.Height, window.IsExtendedIntoWindowDecorations, window.WindowDecorations.ToString(),
                titleBar.MenuContent is not null, NativeMenu.GetIsNativeMenuExported(window), titleBar.CaptionInsets.Left,
                titleBar.CaptionInsets.Right, buttons, MainWindow.Layouts.CurrentPresetId, MainWindow.Layouts.IsModified));
        }
    }

    private void Verify(bool condition, string description)
    {
        (condition ? report.Checks : report.Failures).Add(description);
    }

    private async Task SettleAsync()
    {
        await Task.Delay(200);
        await Dispatcher.UIThread.InvokeAsync(MainWindow.UpdateLayout, DispatcherPriority.Render);
    }

    private static WindowTitleBar FindTitleBar(Window window) => window.GetVisualDescendants().OfType<WindowTitleBar>().Single();
}
