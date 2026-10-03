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
        var previousCommands = Enum.GetValues<WorkbenchCommand>()
            .Where(command => command <= WorkbenchCommand.VIEW_TIMELINE).ToArray();
        if (value.ShortcutBindings.Length != previousCommands.Length ||
            !previousCommands.All(command => value.ShortcutBindings.Any(binding => binding.Command == command)))
        {
            return value;
        }

        var present = value.ShortcutBindings.Select(binding => binding.Command).ToHashSet();
        var additions = ShortcutDefaults.CreateBindings().Where(binding => !present.Contains(binding.Command));
        return value with { ShortcutBindings = value.ShortcutBindings.Concat(additions).ToImmutableArray() };
    }
}
