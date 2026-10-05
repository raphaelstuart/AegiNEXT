using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Windowing;

namespace AegiNext.Desktop.Views;

public sealed partial class MainWindow
{
    private void OpenSettings(SettingsPage? page = null)
    {
        if (settingsWindow is { } existing)
        {
            if (page is { } requested)
            {
                existing.SelectPage(requested);
            }
            existing.Activate();
            return;
        }

        var window = new SettingsWindow(Session.Preferences);
        settingsWindow = window;
        window.SetSubtitlePositionMeasurement(Session.MeasureStylePosition);
        window.UpdateShortcuts(Session.Preferences.ShortcutBindings);
        window.UpdateStyles(Session.StyleLibrary.Snapshot.Presets);
        window.UpdateEffects(Session.EffectScriptLibrary.Snapshot.Presets);
        window.SetEffectOperationBusy(Session.EffectScripts.IsBusy);
        window.UpdateSelectionAvailability(Session.HasSelectedCue && !Session.IsProjectBusy);
        window.SetStyleOperationBusy(Session.Styles.IsBusy);
        window.AppearanceChanged += (_, e) => Session.UpdatePreferences(Session.Preferences with
        {
            Theme = e.Theme, Language = e.Language, WindowMenuOnMac = e.WindowMenuOnMac
        });
        window.ColorsChanged += (_, e) => Session.UpdatePreferences(Session.Preferences with
        {
            AccentColor = e.AccentColor, AudioGraph = e.AudioGraph
        });
        window.ShortcutsChanged += (_, e) =>
        {
            Session.UpdatePreferences(Session.Preferences with { ShortcutBindings = e.Bindings });
            window.UpdateShortcuts(Session.Preferences.ShortcutBindings);
        };
        window.UpsertStyleRequested += (_, e) => Session.Styles.Queue(() => Session.Styles.UpsertAsync(e.Preset));
        window.DeleteStyleRequested += (_, e) => Session.Styles.Queue(() => Session.Styles.DeleteAsync(e.Id));
        window.CaptureStyleRequested += (_, _) => Session.Styles.Queue(Session.Styles.CaptureAsync);
        window.ApplyStyleRequested += (_, e) => Session.Styles.Queue(() => Session.Styles.ApplyAsync(e.Preset));
        window.ImportStylesRequested += (_, _) => Session.Styles.Queue(Session.Styles.ImportAsync);
        window.ExportStylesRequested += (_, _) => Session.Styles.Queue(Session.Styles.ExportAsync);
        window.UpsertEffectRequested += (_, e) => Session.EffectScripts.Queue(() => Session.EffectScripts.UpsertAsync(e.Preset));
        window.DeleteEffectRequested += (_, e) => Session.EffectScripts.Queue(() => Session.EffectScripts.DeleteAsync(e.Id));
        window.ImportEffectRequested += (_, _) => Session.EffectScripts.Queue(Session.EffectScripts.ImportAsync);
        window.ExportEffectRequested += (_, e) => Session.EffectScripts.Queue(() => Session.EffectScripts.ExportAsync(e.Preset));
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(settingsWindow, window))
            {
                settingsWindow = null;
            }
        };
        windowRegistry.Register(window, () => Localization.Get("Settings.Settings"), window.TitleBar,
            WorkbenchWindowRole.SETTINGS);
        if (page is { } selected)
        {
            window.SelectPage(selected);
        }
        window.Show(this);
    }
}
