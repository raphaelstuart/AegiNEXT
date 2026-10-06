using System.ComponentModel;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings.Effects;
using AegiNext.Desktop.Settings.Projects;
using AegiNext.Desktop.Settings.Preview;
using AegiNext.Desktop.Settings.TimingPostProcessor;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Workspace.Diagnostics;
using AegiNext.Desktop.Windowing;
using Avalonia.Controls;

namespace AegiNext.Desktop.Settings;

internal sealed class SettingsWindowCoordinator(DesktopApplicationContext applicationContext,
    Func<Window, IWorkbenchDialogService>? createDialogs = null) : IDisposable
{
    private WorkbenchSession? session;
    private IWorkbenchDialogService? dialogs;
    private WorkbenchPreferences? presentedPreferences;
    private bool disposed;
    private IWindowChrome? chrome;
    internal Task TimingCompletion { get; private set; } = Task.CompletedTask;

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
        dialogs = createDialogs?.Invoke(window) ?? new WindowWorkbenchDialogService(window);
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
        Window?.CloseImmediately();
        EffectScriptErrorReported = null;
    }

    private void Subscribe(SettingsWindow window)
    {
        window.ViewModel.Styles.SaveDraftAsync = preset => RunAsync(() => SaveStylePresetAsync(preset));
        window.ViewModel.Effects.SaveDraftAsync = preset => RunAsync(() =>
            applicationContext.RunEffectOperationAsync(() => applicationContext.EffectScriptLibrary.UpsertAsync(preset)), true);
        window.ViewModel.Styles.ConfirmLeaveAsync = () => dialogs!.ConfirmPresetChangesAsync(false);
        window.ViewModel.Effects.ConfirmLeaveAsync = () => dialogs!.ConfirmPresetChangesAsync(true);
        applicationContext.PreferencesChanged += OnPreferencesChanged;
        applicationContext.StylesChanged += OnStylesChanged;
        applicationContext.EffectsChanged += OnEffectsChanged;
        applicationContext.BusyChanged += OnBusyChanged;
        applicationContext.ErrorChanged += OnErrorChanged;
        window.AppearanceChanged += OnAppearanceChanged;
        window.ColorsChanged += OnColorsChanged;
        window.ShortcutsChanged += OnShortcutsChanged;
        window.PreviewDecodeModeChanged += OnPreviewDecodeModeChanged;
        window.ViewModel.Media.AudioCalibrationChanged += OnAudioCalibrationChanged;
        window.ProjectsChanged += OnProjectsChanged;
        window.PreviewChanged += OnPreviewChanged;
        window.TimingPreferencesChanged += OnTimingPreferencesChanged;
        window.TimingAssociateRequested += OnTimingAssociationRequested;
        window.TimingUnlinkRequested += OnTimingAssociationRequested;
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
            active.AudioClockChanged += OnSessionPreviewDecodeModeChanged;
            active.SelectionChanged += OnSelectionChanged;
            active.StyleLibraryChanged += OnBusyChanged;
            active.EffectLibraryChanged += OnBusyChanged;
            active.ViewModel.PropertyChanged += OnSessionStateChanged;
        }
    }

    private void Unsubscribe(SettingsWindow window)
    {
        window.ViewModel.Styles.SaveDraftAsync = null;
        window.ViewModel.Effects.SaveDraftAsync = null;
        window.ViewModel.Styles.ConfirmLeaveAsync = null;
        window.ViewModel.Effects.ConfirmLeaveAsync = null;
        applicationContext.PreferencesChanged -= OnPreferencesChanged;
        applicationContext.StylesChanged -= OnStylesChanged;
        applicationContext.EffectsChanged -= OnEffectsChanged;
        applicationContext.BusyChanged -= OnBusyChanged;
        applicationContext.ErrorChanged -= OnErrorChanged;
        window.AppearanceChanged -= OnAppearanceChanged;
        window.ColorsChanged -= OnColorsChanged;
        window.ShortcutsChanged -= OnShortcutsChanged;
        window.PreviewDecodeModeChanged -= OnPreviewDecodeModeChanged;
        window.ViewModel.Media.AudioCalibrationChanged -= OnAudioCalibrationChanged;
        window.ProjectsChanged -= OnProjectsChanged;
        window.PreviewChanged -= OnPreviewChanged;
        window.TimingPreferencesChanged -= OnTimingPreferencesChanged;
        window.TimingAssociateRequested -= OnTimingAssociationRequested;
        window.TimingUnlinkRequested -= OnTimingAssociationRequested;
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
            active.AudioClockChanged -= OnSessionPreviewDecodeModeChanged;
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
        RefreshTimingStyles();
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
        RefreshTimingStyles();
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

        window.ViewModel.Media.IsBusy = session?.IsPreviewDecodeModeSwitching == true || session?.IsSwitchingAudioDevice == true;
        window.ViewModel.Media.UpdatePreferences(applicationContext.Preferences);
        var info = session?.PreviewDecodeSessionInfo;
        window.ViewModel.Media.UpdateDecodeStatus(info?.ActiveBackend.ToString(), info?.HardwareConfirmed == true,
            info?.FallbackReason);
        window.ViewModel.Media.UpdateAudioStatus(session?.AudioClock);
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

    private void OnPreviewChanged(object? sender, PreviewSettingsChangedEventArgs e)
    {
        UpdatePreferences(value => value with { SubtitleAuditionMilliseconds = e.SubtitleAuditionMilliseconds });
    }

    private void RefreshTimingStyles()
    {
        if (Window is { } window)
        {
            window.ViewModel.TimingPostProcessor.UpdateStyles(applicationContext.StyleLibrary.Snapshot.Presets);
            window.ViewModel.TimingPostProcessor.IsBusy = applicationContext.StylesBusy;
        }
    }

    private void OnTimingPreferencesChanged(object? sender, TimingPostProcessorPreferencesChangedEventArgs e)
    {
        UpdatePreferences(value => value with { TimingPostProcessor = e.Preferences });
    }

    private void OnTimingAssociationRequested(object? sender, TimingPostProcessorAssociationEventArgs e)
    {
        if (Window is not { } target || applicationContext.StylesBusy || !TimingCompletion.IsCompleted)
        {
            return;
        }

        TimingCompletion = SaveTimingAssociationAsync(target, e);
    }

    private async Task SaveTimingAssociationAsync(SettingsWindow target, TimingPostProcessorAssociationEventArgs request)
    {
        var model = target.ViewModel.TimingPostProcessor;
        model.IsBusy = true;
        target.ShowError(null);
        var saved = false;
        try
        {
            await applicationContext.RunStyleOperationAsync(() =>
                applicationContext.StyleLibrary.SetTimingPostProcessorAsync(request.StyleIds, request.Options));
            saved = true;
        }
        catch (Exception error)
        {
            if (ReferenceEquals(Window, target))
            {
                target.ShowError(Localization.Format("Settings.TimingAssociationFailed", error.Message));
            }
        }
        finally
        {
            model.IsBusy = false;
            if (ReferenceEquals(Window, target))
            {
                RefreshTimingStyles();
                if (saved)
                {
                    if (request.Options is null)
                    {
                        model.ShowUnlinked(request.StyleIds.Count);
                    }
                    else
                    {
                        model.ShowResult(request.StyleIds.Count);
                    }
                }
            }
        }
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

    private void OnAudioCalibrationChanged(object? sender, SettingsAudioCalibrationChangedEventArgs e)
    {
        var active = session;
        _ = RunAsync(async () =>
        {
            if (active is null)
            {
                throw new InvalidOperationException("音频校准需要已打开媒体的工作台。");
            }
            await active.SetAudioCalibrationAsync(e.Calibration);
            RefreshMediaSettings();
        });
    }

    private void OnUpsertStyleRequested(object? sender, SettingsStyleEventArgs e)
    {
        _ = RunAsync(() => SaveStylePresetAsync(e.Preset));
    }

    private Task SaveStylePresetAsync(SubtitleStylePreset preset)
    {
        return applicationContext.RunStyleOperationAsync(() =>
        {
            var current = applicationContext.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == preset.Id);
            var updated = current is null ? preset : preset with { TimingPostProcessor = current.TimingPostProcessor };
            return applicationContext.StyleLibrary.UpsertAsync(updated);
        });
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
            var paths = await service.OpenFilesAsync("ImportStyles", "StyleFiles", ["*.aegistyles"]);
            await applicationContext.StyleLibrary.ImportAsync(paths);
        }));
    }

    private void OnExportStylesRequested(object? sender, SettingsStylesExportEventArgs e)
    {
        var service = dialogs!;
        _ = RunAsync(() => applicationContext.RunStyleOperationAsync(async () =>
        {
            if (e.Presets.Length == 1)
            {
                var path = await service.SaveFileAsync("ExportStyles", "StyleFiles", ["*.aegistyles"], ".aegistyles", "styles.aegistyles");
                if (path is not null)
                {
                    await applicationContext.StyleLibrary.ExportAsync(e.Presets[0], path);
                }
            }
            else if (e.Presets.Length > 1 && await service.OpenFolderAsync("ExportStyles") is { } directory)
            {
                await PresetBatchExporter.ExportStylesAsync(e.Presets, directory);
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
            var paths = await service.OpenFilesAsync("ImportEffectScripts", "EffectScriptFiles", ["*.aegifx"]);
            await applicationContext.EffectScriptLibrary.ImportAsync(paths);
        }), true);
    }

    private void OnExportEffectRequested(object? sender, SettingsEffectsExportEventArgs e)
    {
        var service = dialogs!;
        _ = RunAsync(() => applicationContext.RunEffectOperationAsync(async () =>
        {
            if (e.Presets.Length == 1)
            {
                var preset = e.Presets[0];
                var script = EffectScriptParser.Parse(preset.Source);
                var path = await service.SaveFileAsync("ExportEffectScripts", "EffectScriptFiles", ["*.aegifx"], ".aegifx", script.Id + ".aegifx");
                if (path is not null)
                {
                    await applicationContext.EffectScriptLibrary.ExportAsync(preset, path);
                }
            }
            else if (e.Presets.Length > 1 && await service.OpenFolderAsync("ExportEffectScripts") is { } directory)
            {
                await PresetBatchExporter.ExportEffectsAsync(e.Presets, directory);
            }
        }), true);
    }

    private async Task<bool> RunAsync(Func<Task> operation, bool effectOperation = false)
    {
        if (disposed || Window is not { } targetWindow)
        {
            return false;
        }

        var targetSession = session;
        targetWindow.ShowError(null);
        try
        {
            await operation();
            return true;
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
        return false;
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
