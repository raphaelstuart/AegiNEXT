using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Windowing;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Panels.Log;
using AegiNext.Desktop.Workspace.Diagnostics;

namespace AegiNext.Desktop.Views;

public sealed partial class MainWindow
{
    private void OpenSettings(SettingsPage? page = null)
    {
        _ = settingsCoordinator.OpenAsync(this, Session, page, window =>
        {
            var settings = (SettingsWindow)window;
            windowRegistry.Register(settings, () => Localization.Get("Settings.Settings"), settings.TitleBar,
                WorkbenchWindowRole.SETTINGS);
        });
    }

    private void RevealEffectScriptError(WorkbenchLogEntry entry)
    {
        if (closing || disposeTask is not null || Session.IsClosing)
        {
            return;
        }

        Activate();
        layouts.Activate(WorkbenchPanelIds.LOG);
        ((LogPanelView)panels[WorkbenchPanelIds.LOG]).RevealEntry(entry);
    }
}
