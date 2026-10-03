using System.Windows.Input;
using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Input;

namespace AegiNext.Desktop.Menus;

internal sealed class WorkbenchMenuCatalog(Func<WorkbenchCommand, ICommand> commandProvider)
{
    internal static readonly WorkbenchMenuGroup[] Groups =
    [
        new("File", [WorkbenchCommand.NEW_PROJECT, WorkbenchCommand.OPEN_PROJECT, WorkbenchCommand.OPEN_MEDIA, null,
            WorkbenchCommand.SAVE_PROJECT, WorkbenchCommand.SAVE_PROJECT_AS, null,
            WorkbenchCommand.IMPORT_SUBTITLES, WorkbenchCommand.EXPORT_SUBTITLES, WorkbenchCommand.EXPORT_VIDEO, null,
            WorkbenchCommand.EXIT]),
        new("Edit", [WorkbenchCommand.UNDO, WorkbenchCommand.REDO, null, WorkbenchCommand.OPEN_SETTINGS]),
        new("View", [WorkbenchCommand.VIEW_PREVIEW, WorkbenchCommand.VIEW_TIMELINE, WorkbenchCommand.VIEW_SUBTITLES,
            WorkbenchCommand.VIEW_STYLES, WorkbenchCommand.VIEW_EFFECTS, WorkbenchCommand.VIEW_EXPORT]),
        new("Layouts", [WorkbenchCommand.LAYOUT_SAVE, WorkbenchCommand.LAYOUT_SAVE_AS,
            WorkbenchCommand.LAYOUT_MANAGE, WorkbenchCommand.LAYOUT_RESTORE_DEFAULT]),
        new("Playback", [WorkbenchCommand.PLAY_PAUSE, WorkbenchCommand.SEEK_BACKWARD, WorkbenchCommand.SEEK_FORWARD]),
        new("Subtitles", [WorkbenchCommand.TIMING_ENTER, WorkbenchCommand.TIMING_EXIT, null,
            WorkbenchCommand.ADD_SUBTITLE, WorkbenchCommand.DELETE_SUBTITLE, WorkbenchCommand.SPLIT_SUBTITLE,
            WorkbenchCommand.MERGE_SUBTITLE])
    ];

    private WorkbenchPreferences preferences = new();
    internal IReadOnlyList<LayoutMenuChoice> LayoutChoices { get; private set; } = [];
    internal bool IsLayoutModified { get; private set; }
    internal event EventHandler? Changed;

    internal ICommand GetCommand(WorkbenchCommand command) => commandProvider(command);
    internal static string GetLabel(WorkbenchCommand command) => SettingsText.Get(command.ToString());

    internal KeyGesture? GetGesture(WorkbenchCommand command)
    {
        return ShortcutConfiguration.Parse(preferences.ShortcutBindings.Single(value => value.Command == command).Gesture,
            OperatingSystem.IsMacOS());
    }

    internal string GetGestureLabel(WorkbenchCommand command)
    {
        var text = preferences.ShortcutBindings.Single(value => value.Command == command).Gesture;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return text.Replace("CmdOrCtrl", OperatingSystem.IsMacOS() ? "⌘" : "Ctrl", StringComparison.Ordinal);
    }

    internal void Update(WorkbenchPreferences value)
    {
        preferences = value;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    internal void UpdateLayouts(IReadOnlyList<LayoutMenuChoice> choices, bool modified)
    {
        LayoutChoices = choices;
        IsLayoutModified = modified;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
