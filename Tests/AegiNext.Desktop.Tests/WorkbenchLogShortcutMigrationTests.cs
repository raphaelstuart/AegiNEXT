using System.Collections.Immutable;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

public sealed class WorkbenchLogShortcutMigrationTests
{
    [Fact]
    public void PreviousCompleteBindingsKeepCustomAndDisabledGesturesWhenLogIsAdded()
    {
        var previous = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.LAYOUT_RESTORE_DEFAULT)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.OPEN_PROJECT => binding with { Gesture = "CmdOrCtrl+Alt+O" },
                WorkbenchCommand.SAVE_PROJECT => binding with { Gesture = string.Empty },
                _ => binding
            }).ToImmutableArray();
        var restored = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });
        restored.Validate();
        Assert.Equal(previous, restored.ShortcutBindings.Where(binding => binding.Command <= WorkbenchCommand.LAYOUT_RESTORE_DEFAULT));
        Assert.Equal(string.Empty, Assert.Single(restored.ShortcutBindings, binding => binding.Command == WorkbenchCommand.VIEW_LOG).Gesture);
    }
}
