using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;

namespace AegiNext.Desktop.Menus;

internal sealed class WorkbenchApplicationMenu : IDisposable
{
    private readonly WorkbenchMenuCatalog catalog;
    private readonly NativeMenu menu;
    private readonly NativeMenuItem settings;

    internal WorkbenchApplicationMenu(Avalonia.Application application, WorkbenchMenuCatalog catalog)
    {
        this.catalog = catalog;
        menu = NativeMenu.GetMenu(application) ?? new NativeMenu();
        if (NativeMenu.GetMenu(application) is null)
        {
            NativeMenu.SetMenu(application, menu);
        }

        settings = new() { Command = catalog.GetCommand(WorkbenchCommand.OPEN_SETTINGS) };
        menu.Items.Insert(0, settings);
        catalog.Changed += OnCatalogChanged;
        Refresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        catalog.Changed -= OnCatalogChanged;
        menu.Items.Remove(settings);
    }

    private void OnCatalogChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void Refresh()
    {
        settings.Header = Localization.Get("Settings." + (WorkbenchCommand.OPEN_SETTINGS.ToString()));
        settings.Gesture = catalog.GetGesture(WorkbenchCommand.OPEN_SETTINGS);
    }
}
