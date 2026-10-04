using System.Collections.Immutable;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Settings;

internal static class WorkbenchPreferencesMigration
{
    internal static WorkbenchPreferences Upgrade(WorkbenchPreferences value)
    {
        if (value.Version != 1 || value.ShortcutBindings.IsDefault)
        {
            return value;
        }

        ShortcutConfiguration.Validate(value.ShortcutBindings);
        var present = value.ShortcutBindings.Select(binding => binding.Command).ToHashSet();
        var commands = Enum.GetValues<WorkbenchCommand>();
        var legacy = commands.Where(command => command <= WorkbenchCommand.VIEW_TIMELINE);
        var previous = commands.Where(command => command <= WorkbenchCommand.LAYOUT_RESTORE_DEFAULT);
        if (!present.SetEquals(legacy) && !present.SetEquals(previous))
        {
            return value;
        }

        var additions = ShortcutDefaults.CreateBindings().Where(binding => !present.Contains(binding.Command));
        return value with { ShortcutBindings = value.ShortcutBindings.Concat(additions).ToImmutableArray() };
    }
}
