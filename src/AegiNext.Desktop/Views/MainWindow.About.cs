using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Windowing;

namespace AegiNext.Desktop.Views;

public sealed partial class MainWindow
{
    private AboutWindow? aboutWindow;

    private void OpenAbout()
    {
        if (aboutWindow is { } existing)
        {
            existing.Activate();
            return;
        }

        var window = new AboutWindow();
        aboutWindow = window;
        window.Closed += OnAboutClosed;
        windowRegistry.Register(window, () => Localization.Get("About.Title"), window.TitleBar,
            WorkbenchWindowRole.AUXILIARY);
        window.Show(this);
    }

    private void OnAboutClosed(object? sender, EventArgs e)
    {
        aboutWindow!.Closed -= OnAboutClosed;
        aboutWindow = null;
    }
}
