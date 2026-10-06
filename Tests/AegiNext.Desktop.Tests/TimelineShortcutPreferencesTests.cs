using System.Collections.Immutable;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Input;

namespace AegiNext.Desktop.Tests;

public sealed class TimelineShortcutPreferencesTests
{
    [Fact]
    public void PreviousCompleteMaskCommandsAppendCopyAndPasteWithoutChangingCustomizedOrDisabledBindings()
    {
        var previous = PreviousBindings();
        var original = new WorkbenchPreferences
        {
            ShortcutBindings = previous,
            Language = "zh-CN",
            TimelineSnapEnabled = false,
            TimelineStepEnabled = true
        };

        var upgraded = WorkbenchPreferencesMigration.Upgrade(original);
        upgraded.Validate();

        Assert.Equal(previous.AsEnumerable(), upgraded.ShortcutBindings.Take(previous.Length));
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length, upgraded.ShortcutBindings.Length);
        Assert.Equal(ShortcutDefaults.CreateBindings().Where(binding => binding.Command > WorkbenchCommand.VIEW_MASKS).Select(binding => binding.Command),
            upgraded.ShortcutBindings.Skip(previous.Length).Select(binding => binding.Command));
        Assert.Equal("CmdOrCtrl+C", Binding(upgraded, WorkbenchCommand.COPY_CLIPS).Gesture);
        Assert.Equal("CmdOrCtrl+V", Binding(upgraded, WorkbenchCommand.PASTE_CLIPS).Gesture);
        Assert.Equal("CmdOrCtrl+Alt+O", Binding(upgraded, WorkbenchCommand.OPEN_PROJECT).Gesture);
        Assert.Equal(string.Empty, Binding(upgraded, WorkbenchCommand.SAVE_PROJECT).Gesture);
        Assert.Equal(original.Version, upgraded.Version);
        Assert.Equal(original.Language, upgraded.Language);
        Assert.Equal(original.TimelineSnapEnabled, upgraded.TimelineSnapEnabled);
        Assert.Equal(original.TimelineStepEnabled, upgraded.TimelineStepEnabled);
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void OccupiedCopyAndPasteGesturesOnlyDisableTheConflictingNewDefaults(bool copyOccupied, bool pasteOccupied)
    {
        var previous = PreviousBindings(copyOccupied, pasteOccupied);

        var upgraded = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });
        upgraded.Validate();

        Assert.Equal(previous.AsEnumerable(), upgraded.ShortcutBindings.Take(previous.Length));
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length, upgraded.ShortcutBindings.Length);
        Assert.Equal(copyOccupied ? string.Empty : "CmdOrCtrl+C", Binding(upgraded, WorkbenchCommand.COPY_CLIPS).Gesture);
        Assert.Equal(pasteOccupied ? string.Empty : "CmdOrCtrl+V", Binding(upgraded, WorkbenchCommand.PASTE_CLIPS).Gesture);
        Assert.Equal(copyOccupied ? "CmdOrCtrl+C" : "Space", Binding(upgraded, WorkbenchCommand.PLAY_PAUSE).Gesture);
        Assert.Equal(pasteOccupied ? "CmdOrCtrl+V" : string.Empty, Binding(upgraded, WorkbenchCommand.EXIT).Gesture);
        Assert.Equal(string.Empty, Binding(upgraded, WorkbenchCommand.SAVE_PROJECT).Gesture);
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded));
    }

    [Theory]
    [InlineData(true, KeyModifiers.Meta, KeyModifiers.Control)]
    [InlineData(false, KeyModifiers.Control, KeyModifiers.Meta)]
    public void CopyAndPasteUseOnlyThePlatformPrimaryModifierAndLeaveTextEditingKeysUnclaimed(
        bool isMacOs, KeyModifiers primary, KeyModifiers other)
    {
        var defaults = ShortcutDefaults.CreateBindings();
        var copy = ShortcutConfiguration.Parse(defaults.Single(binding => binding.Command == WorkbenchCommand.COPY_CLIPS).Gesture, isMacOs);
        var paste = ShortcutConfiguration.Parse(defaults.Single(binding => binding.Command == WorkbenchCommand.PASTE_CLIPS).Gesture, isMacOs);
        var router = new ShortcutRouter(defaults, isMacOs);

        Assert.NotNull(copy);
        Assert.Equal(Key.C, copy.Key);
        Assert.Equal(primary, copy.KeyModifiers);
        Assert.NotNull(paste);
        Assert.Equal(Key.V, paste.Key);
        Assert.Equal(primary, paste.KeyModifiers);
        Assert.True(router.TryResolve(Key.C, primary, false, out var copyCommand));
        Assert.Equal(WorkbenchCommand.COPY_CLIPS, copyCommand);
        Assert.True(router.TryResolve(Key.V, primary, false, out var pasteCommand));
        Assert.Equal(WorkbenchCommand.PASTE_CLIPS, pasteCommand);
        Assert.False(router.TryResolve(Key.C, other, false, out _));
        Assert.False(router.TryResolve(Key.V, other, false, out _));
        Assert.False(router.TryResolve(Key.C, primary | KeyModifiers.Alt, false, out _));
        Assert.False(router.TryResolve(Key.V, primary | KeyModifiers.Shift, false, out _));
        Assert.False(router.TryResolve(Key.C, primary, true, out _));
        Assert.False(router.TryResolve(Key.V, primary, true, out _));
        Assert.False(router.TryResolve(Key.Delete, KeyModifiers.None, true, out _));
    }

    [Fact]
    public void AlreadyCurrentCustomizedAndDisabledClipBindingsAreNotRestoredByMigration()
    {
        var original = new WorkbenchPreferences
        {
            ShortcutBindings = ShortcutDefaults.CreateBindings().Select(binding => binding.Command switch
            {
                WorkbenchCommand.COPY_CLIPS => binding with { Gesture = "F6" },
                WorkbenchCommand.PASTE_CLIPS => binding with { Gesture = string.Empty },
                _ => binding
            }).ToImmutableArray()
        };

        var restored = WorkbenchPreferencesMigration.Upgrade(original);
        restored.Validate();

        Assert.Same(original, restored);
        Assert.Equal("F6", Binding(restored, WorkbenchCommand.COPY_CLIPS).Gesture);
        Assert.Equal(string.Empty, Binding(restored, WorkbenchCommand.PASTE_CLIPS).Gesture);
    }

    private static ImmutableArray<ShortcutBinding> PreviousBindings(bool copyOccupied = false, bool pasteOccupied = false)
    {
        return ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.VIEW_MASKS)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.OPEN_PROJECT => binding with { Gesture = "CmdOrCtrl+Alt+O" },
                WorkbenchCommand.SAVE_PROJECT => binding with { Gesture = string.Empty },
                WorkbenchCommand.PLAY_PAUSE when copyOccupied => binding with { Gesture = "CmdOrCtrl+C" },
                WorkbenchCommand.EXIT when pasteOccupied => binding with { Gesture = "CmdOrCtrl+V" },
                _ => binding
            }).ToImmutableArray();
    }

    private static ShortcutBinding Binding(WorkbenchPreferences preferences, WorkbenchCommand command)
    {
        return Assert.Single(preferences.ShortcutBindings, binding => binding.Command == command);
    }
}
