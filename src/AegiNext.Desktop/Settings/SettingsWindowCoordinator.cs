using System.ComponentModel;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings.Effects;
using AegiNext.Desktop.Settings.Projects;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Workspace.Diagnostics;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;

namespace AegiNext.Desktop.Settings;

internal sealed class SettingsWindowCoordinator(DesktopApplicationContext applicationContext) : IDisposable
{
    private WorkbenchSession? session;
    private IWorkbenchDialogService? dialogs;
    private WorkbenchPreferences? presentedPreferences;
    private bool disposed;
    private IWindowChrome? chrome;

    internal event Action<WorkbenchLogEntry>? EffectScriptErrorReported;
    internal SettingsWindow? Window { get; private set; }

    internal Task OpenAsync(Window owner, WorkbenchSession? session = null, SettingsPage? page = null,
        Action<Window>? registerWindow = null, bool modal = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(owner);
        if (Window is { } existing)
        {
            if (page is { } requested)
            {
                existing.SelectPage(requested);
            }

            existing.Activate();
            return Task.CompletedTask;
        }

        var window = new SettingsWindow(applicationContext.Preferences);
        window.ViewModel.Styles.SetFonts(applicationContext.Fonts);
        Window = window;
        this.session = session;
        presentedPreferences = applicationContext.Preferences;
        dialogs = new WindowWorkbenchDialogService(window);
        try
        {
            Subscribe(window);
            window.SetSubtitlePositionMeasurement(session is null ? MeasureDefaultPosition : session.MeasureStylePosition);
            window.UpdateShortcuts(applicationContext.Preferences.ShortcutBindings);
            window.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
            window.UpdateEffects(applicationContext.EffectScriptLibrary.Snapshot.Presets);
            RefreshAvailability();
            RefreshMediaSettings();
            window.ShowError(applicationContext.LastError?.Message);
            if (page is { } selected)
            {
                window.SelectPage(selected);
            }

            if (registerWindow is null)
            {
                chrome = WindowChrome.Attach(window, window.TitleBar);
            }
            else
            {
                registerWindow(window);
            }
            if (modal)
            {
                return window.ShowDialog(owner);
            }

            window.Show(owner);
            return Task.CompletedTask;
        }
        catch
        {
            Unsubscribe(window);
            Window = null;
            this.session = null;
            dialogs = null;
            presentedPreferences = null;
            window.Close();
            chrome?.Dispose();
            chrome = null;
            throw;
        }
    }

    internal void Close()
    {
        Window?.Close();
    }

    /// <summary>关闭设置并解除窗口、共享资源与工程会话事件。</summary>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Close();
        EffectScriptErrorReported = null;
    }

    private void Subscribe(SettingsWindow window)
    {
        applicationContext.PreferencesChanged += OnPreferencesChanged;
        applicationContext.StylesChanged += OnStylesChanged;
        applicationContext.EffectsChanged += OnEffectsChanged;
        applicationContext.BusyChanged += OnBusyChanged;
        applicationContext.ErrorChanged += OnErrorChanged;
        window.AppearanceChanged += OnAppearanceChanged;
        window.ColorsChanged += OnColorsChanged;
        window.ShortcutsChanged += OnShortcutsChanged;
        window.PreviewDecodeModeChanged += OnPreviewDecodeModeChanged;
        window.ProjectsChanged += OnProjectsChanged;
        window.UpsertStyleRequested += OnUpsertStyleRequested;
        window.DeleteStyleRequested += OnDeleteStyleRequested;
        window.CaptureStyleRequested += OnCaptureStyleRequested;
        window.ApplyStyleRequested += OnApplyStyleRequested;
        window.ImportStylesRequested += OnImportStylesRequested;
        window.ExportStylesRequested += OnExportStylesRequested;
        window.EffectValidationFailed += OnEffectValidationFailed;
        window.UpsertEffectRequested += OnUpsertEffectRequested;
        window.DeleteEffectRequested += OnDeleteEffectRequested;
        window.ImportEffectRequested += OnImportEffectRequested;
        window.ExportEffectRequested += OnExportEffectRequested;
        window.Closed += OnWindowClosed;
        if (session is { } active)
        {
            active.PreviewDecodeModeChanged += OnSessionPreviewDecodeModeChanged;
            active.SelectionChanged += OnSelectionChanged;
            active.StyleLibraryChanged += OnBusyChanged;
            active.EffectLibraryChanged += OnBusyChanged;
            active.ViewModel.PropertyChanged += OnSessionStateChanged;
        }
    }

    private void Unsubscribe(SettingsWindow window)
    {
        applicationContext.PreferencesChanged -= OnPreferencesChanged;
        applicationContext.StylesChanged -= OnStylesChanged;
        applicationContext.EffectsChanged -= OnEffectsChanged;
        applicationContext.BusyChanged -= OnBusyChanged;
        applicationContext.ErrorChanged -= OnErrorChanged;
        window.AppearanceChanged -= OnAppearanceChanged;
        window.ColorsChanged -= OnColorsChanged;
        window.ShortcutsChanged -= OnShortcutsChanged;
        window.PreviewDecodeModeChanged -= OnPreviewDecodeModeChanged;
        window.ProjectsChanged -= OnProjectsChanged;
        window.UpsertStyleRequested -= OnUpsertStyleRequested;
        window.DeleteStyleRequested -= OnDeleteStyleRequested;
        window.CaptureStyleRequested -= OnCaptureStyleRequested;
        window.ApplyStyleRequested -= OnApplyStyleRequested;
        window.ImportStylesRequested -= OnImportStylesRequested;
        window.ExportStylesRequested -= OnExportStylesRequested;
        window.EffectValidationFailed -= OnEffectValidationFailed;
        window.UpsertEffectRequested -= OnUpsertEffectRequested;
        window.DeleteEffectRequested -= OnDeleteEffectRequested;
        window.ImportEffectRequested -= OnImportEffectRequested;
        window.ExportEffectRequested -= OnExportEffectRequested;
        window.Closed -= OnWindowClosed;
        if (session is { } active)
        {
            active.PreviewDecodeModeChanged -= OnSessionPreviewDecodeModeChanged;
            active.SelectionChanged -= OnSelectionChanged;
            active.StyleLibraryChanged -= OnBusyChanged;
            active.EffectLibraryChanged -= OnBusyChanged;
            active.ViewModel.PropertyChanged -= OnSessionStateChanged;
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window && ReferenceEquals(Window, window))
        {
            Unsubscribe(window);
            chrome?.Dispose();
            chrome = null;
            Window = null;
            session = null;
            dialogs = null;
            presentedPreferences = null;
        }
    }

    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        var current = applicationContext.Preferences;
        var previous = presentedPreferences;
        presentedPreferences = current;
        Window?.UpdatePreferences(current);
        if (previous is null || !previous.ShortcutBindings.SequenceEqual(current.ShortcutBindings))
        {
            Window?.UpdateShortcuts(current.ShortcutBindings);
        }
    }

    private void OnStylesChanged(object? sender, EventArgs e)
    {
        Window?.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
    }

    private void OnEffectsChanged(object? sender, EventArgs e)
    {
        Window?.UpdateEffects(applicationContext.EffectScriptLibrary.Snapshot.Presets);
    }

    private void OnBusyChanged(object? sender, EventArgs e)
    {
        RefreshAvailability();
    }

    private void OnErrorChanged(object? sender, EventArgs e)
    {
        Window?.ShowError(applicationContext.LastError?.Message);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        RefreshAvailability();
    }

    private void OnSessionStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(WorkbenchViewModel.IsBusy))
        {
            RefreshAvailability();
        }
        else if (e.PropertyName == nameof(WorkbenchViewModel.Error))
        {
            Window?.ShowError(session?.ViewModel.Error);
        }
    }

    private void RefreshAvailability()
    {
        var projectBusy = session is { } active && (active.IsProjectBusy || active.IsClosing);
        Window?.SetStyleOperationBusy(applicationContext.StylesBusy || projectBusy || session?.Styles.IsBusy == true);
        Window?.SetEffectOperationBusy(applicationContext.EffectsBusy || projectBusy || session?.EffectScripts.IsBusy == true);
        Window?.UpdateSelectionAvailability(session is { HasSelectedCue: true, IsProjectBusy: false, IsClosing: false });
    }

    private void OnSessionPreviewDecodeModeChanged(object? sender, EventArgs e)
    {
        RefreshMediaSettings();
    }

    private void RefreshMediaSettings()
    {
        if (Window is not { } window)
        {
            return;
        }

        window.ViewModel.Media.IsBusy = session?.IsPreviewDecodeModeSwitching == true;
        window.ViewModel.Media.UpdatePreferences(applicationContext.Preferences);
        var info = session?.PreviewDecodeSessionInfo;
        window.ViewModel.Media.UpdateDecodeStatus(info?.ActiveBackend.ToString(), info?.HardwareConfirmed == true,
            info?.FallbackReason);
    }

    private void OnAppearanceChanged(object? sender, SettingsAppearanceChangedEventArgs e)
    {
        UpdatePreferences(value => value with
        {
            Theme = e.Theme, Language = e.Language, WindowMenuOnMac = e.WindowMenuOnMac
        });
    }

    private void OnColorsChanged(object? sender, SettingsColorsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { AccentColor = e.AccentColor, AudioGraph = e.AudioGraph, TimelineClips = e.TimelineClips });
    }

    private void OnProjectsChanged(object? sender, ProjectPreferencesChangedEventArgs e)
    {
        UpdatePreferences(value => value with { Projects = e.Preferences });
    }

    private void OnShortcutsChanged(object? sender, SettingsShortcutsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { ShortcutBindings = e.Bindings });
    }

    private void UpdatePreferences(Func<WorkbenchPreferences, WorkbenchPreferences> update)
    {
        _ = RunAsync(() =>
        {
            applicationContext.UpdatePreferences(update);
            return Task.CompletedTask;
        });
    }

    private void OnPreviewDecodeModeChanged(object? sender, SettingsPreviewDecodeModeChangedEventArgs e)
    {
        var active = session;
        var targetWindow = Window;
        _ = RunAsync(async () =>
        {
            if (active is null)
            {
                applicationContext.UpdatePreferences(value => value with { PreviewDecodeMode = e.Mode });
            }
            else if (!await active.SetPreviewDecodeModeAsync(e.Mode) && active.LastError is { } error)
            {
                if (ReferenceEquals(Window, targetWindow))
                {
                    targetWindow?.ShowError(error.Message);
                }
            }

            RefreshMediaSettings();
        });
    }

    private void OnUpsertStyleRequested(object? sender, SettingsStyleEventArgs e)
    {
        _ = RunAsync(() => applicationContext.RunStyleOperationAsync(() => applicationContext.StyleLibrary.UpsertAsync(e.Preset)));
    }

    private void OnDeleteStyleRequested(object? sender, SettingsStyleDeleteEventArgs e)
    {
        _ = RunAsync(() => applicationContext.RunStyleOperationAsync(() => applicationContext.StyleLibrary.RemoveAsync(e.Id)));
    }

    private void OnCaptureStyleRequested(object? sender, EventArgs e)
    {
        if (session is { } active)
        {
            _ = RunAsync(() =>
            {
                active.Styles.Queue(active.Styles.CaptureAsync);
                return active.Styles.Completion;
            });
        }
    }

    private void OnApplyStyleRequested(object? sender, SettingsStyleEventArgs e)
    {
        if (session is { } active)
        {
            _ = RunAsync(() =>
            {
                active.Styles.Queue(() => active.Styles.ApplyAsync(e.Preset));
                return active.Styles.Completion;
            });
        }
    }

    private void OnImportStylesRequested(object? sender, EventArgs e)
    {
        var service = dialogs!;
        _ = RunAsync(() => applicationContext.RunStyleOperationAsync(async () =>
        {
            var path = await service.OpenFileAsync("ImportStyles", "StyleFiles", ["*.aegistyles"]);
            if (path is not null)
            {
                await applicationContext.StyleLibrary.ImportAsync(path);
            }
        }));
    }

    private void OnExportStylesRequested(object? sender, EventArgs e)
    {
        var service = dialogs!;
        _ = RunAsync(() => applicationContext.RunStyleOperationAsync(async () =>
        {
            var path = await service.SaveFileAsync("ExportStyles", "StyleFiles", ["*.aegistyles"], ".aegistyles", "styles.aegistyles");
            if (path is not null)
            {
                await applicationContext.StyleLibrary.ExportAsync(path);
            }
        }));
    }

    private void OnEffectValidationFailed(object? sender, EffectScriptValidationFailedEventArgs e)
    {
        if (session is { IsClosing: false } active)
        {
            EffectScriptErrorReported?.Invoke(active.LogError("Effects", e.Error));
        }
    }

    private void OnUpsertEffectRequested(object? sender, SettingsEffectEventArgs e)
    {
        _ = RunAsync(() => applicationContext.RunEffectOperationAsync(() => applicationContext.EffectScriptLibrary.UpsertAsync(e.Preset)), true);
    }

    private void OnDeleteEffectRequested(object? sender, SettingsEffectDeleteEventArgs e)
    {
        _ = RunAsync(() => applicationContext.RunEffectOperationAsync(() => applicationContext.EffectScriptLibrary.RemoveAsync(e.Id)), true);
    }

    private void OnImportEffectRequested(object? sender, EventArgs e)
    {
        var service = dialogs!;
        _ = RunAsync(() => applicationContext.RunEffectOperationAsync(async () =>
        {
            var path = await service.OpenFileAsync("ImportEffectScripts", "EffectScriptFiles", ["*.aegifx"]);
            if (path is not null)
            {
                await applicationContext.EffectScriptLibrary.ImportAsync(path);
            }
        }), true);
    }

    private void OnExportEffectRequested(object? sender, SettingsEffectEventArgs e)
    {
        var service = dialogs!;
        _ = RunAsync(() => applicationContext.RunEffectOperationAsync(async () =>
        {
            var script = EffectScriptParser.Parse(e.Preset.Source);
            var path = await service.SaveFileAsync("ExportEffectScripts", "EffectScriptFiles", ["*.aegifx"], ".aegifx", script.Id + ".aegifx");
            if (path is not null)
            {
                await EffectScriptPresetStore.WriteScriptAsync(e.Preset.Source, path);
            }
        }), true);
    }

    private async Task RunAsync(Func<Task> operation, bool effectOperation = false)
    {
        if (disposed || Window is not { } targetWindow)
        {
            return;
        }

        var targetSession = session;
        targetWindow.ShowError(null);
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception error)
        {
            if (ReferenceEquals(Window, targetWindow))
            {
                targetWindow.ShowError(error.Message);
            }

            if (effectOperation)
            {
                ReportEffectError(targetSession, error);
            }
            else if (targetSession is { IsClosing: false })
            {
                targetSession.ShowError(error);
            }
        }
    }

    private void ReportEffectError(WorkbenchSession? targetSession, Exception error)
    {
        if (targetSession is { IsClosing: false })
        {
            targetSession.ShowError(error, false);
            EffectScriptErrorReported?.Invoke(targetSession.LogError("Effects", error));
        }
    }

    private SubtitlePositionMeasurement MeasureDefaultPosition(SubtitleStylePreset preset)
    {
        var document = new ProjectDocument();
        return Rendering.SubtitleStylePositionMeasurer.Measure(preset, document.Width, document.Height,
            Localization.Get("Workbench.SubtitlePreviewText"), applicationContext.Fonts.Catalog);
    }
}
