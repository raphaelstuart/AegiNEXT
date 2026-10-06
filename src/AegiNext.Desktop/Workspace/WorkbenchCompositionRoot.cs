using AegiNext.Application;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Media.Encoding;

namespace AegiNext.Desktop.Workspace;

internal static class WorkbenchCompositionRoot
{
    internal static WorkbenchSession Create(IWorkbenchDialogService dialogs,
        Func<Action<VideoPreviewUpdate>, VideoPreviewController>? controllerFactory = null,
        IWorkbenchExportService? exportService = null,
        WorkbenchStartupPreferences? startup = null,
        DesktopApplicationContext? applicationContext = null)
    {
        var editor = new ProjectEditor();
        if (applicationContext is null)
        {
            startup ??= LoadPreferences();
        }

        return new(dialogs, controllerFactory, editor: editor, preferencesStore: startup?.Store,
            exportService: exportService ?? new VideoWorkbenchExportService(new VideoExporter()),
            initialPreferences: startup?.Preferences, applicationContext: applicationContext);
    }

    internal static WorkbenchStartupPreferences LoadPreferences()
    {
        var store = new WorkbenchPreferencesStore(Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY"));
        var preferences = store.Load();
        ApplyLanguagePreference(preferences.Language);
        return new(store, preferences);
    }

    internal static void ApplyLanguagePreference(string languageId)
    {
        var selected = string.Equals(languageId, "system", StringComparison.OrdinalIgnoreCase)
            ? "system"
            : Localization.KnownLanguages.FirstOrDefault(info =>
                string.Equals(info.LanguageID, languageId, StringComparison.OrdinalIgnoreCase))?.LanguageID ?? "en-US";
        if (Localization.SelectedLanguageID != selected)
        {
            Localization.SetLanguage(selected);
        }
    }
}
