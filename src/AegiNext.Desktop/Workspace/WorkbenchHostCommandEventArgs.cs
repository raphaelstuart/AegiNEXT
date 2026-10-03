using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Workspace;

internal sealed class WorkbenchHostCommandEventArgs(WorkbenchCommand command, SettingsPage? settingsPage = null) : EventArgs
{
    public WorkbenchCommand Command { get; } = command;
    public SettingsPage? SettingsPage { get; } = settingsPage;
}
