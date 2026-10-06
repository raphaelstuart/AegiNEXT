using System.Collections.Immutable;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Input;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class EndTextInputShortcutTests
{
    [Fact]
    public void DefaultExitCommandWorksInTextWithoutReleasingOtherEditingKeys()
    {
        Assert.True(Enum.TryParse<WorkbenchCommand>("END_TEXT_INPUT", out var exit));
        var router = new ShortcutRouter(ShortcutDefaults.CreateBindings());
        Assert.True(router.TryResolve(Key.Escape, KeyModifiers.None, true, out var resolved));
        Assert.Equal(exit, resolved);
        Assert.False(router.TryResolve(Key.Space, KeyModifiers.None, true, out _));
        Assert.False(router.TryResolve(Key.Left, KeyModifiers.None, true, out _));
    }

    [Fact]
    public void PreviousCompleteGeneralCommandsAppendMaskViewAndClipCommandsWithoutChangingCustomizedBindings()
    {
        var previous = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.OPEN_ABOUT)
            .Select(binding => binding.Command == WorkbenchCommand.END_TEXT_INPUT ? binding with { Gesture = "F6" } : binding).ToImmutableArray();
        var upgraded = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });
        upgraded.Validate();
        Assert.Equal(previous.AsEnumerable(), upgraded.ShortcutBindings.Where(binding => binding.Command <= WorkbenchCommand.OPEN_ABOUT));
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length - previous.Length, upgraded.ShortcutBindings.Length - previous.Length);
        Assert.Equal(string.Empty, Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.VIEW_MASKS).Gesture);
        Assert.Equal("CmdOrCtrl+C", Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.COPY_CLIPS).Gesture);
        Assert.Equal("CmdOrCtrl+V", Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.PASTE_CLIPS).Gesture);
        Assert.Equal("F6", Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.END_TEXT_INPUT).Gesture);
    }

    [Fact]
    public void CustomAndDisabledExitBindingsAreRespectedInText()
    {
        Assert.True(Enum.TryParse<WorkbenchCommand>("END_TEXT_INPUT", out var exit));
        var router = new ShortcutRouter([new(exit, "Shift+F2")]);
        Assert.False(router.TryResolve(Key.Escape, KeyModifiers.None, true, out _));
        Assert.True(router.TryResolve(Key.F2, KeyModifiers.Shift, true, out var resolved));
        Assert.Equal(exit, resolved);
        Assert.False(router.TryResolve(Key.F2, KeyModifiers.None, true, out _));
        router = new([new(exit, string.Empty)]);
        Assert.False(router.TryResolve(Key.Escape, KeyModifiers.None, true, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviousCompleteSettingsPreserveExistingBindingsAndAvoidEscapeConflict(bool occupied)
    {
        Assert.True(Enum.TryParse<WorkbenchCommand>("END_TEXT_INPUT", out var exit));
        var previous = ShortcutDefaults.CreateBindings()
            .Where(binding => binding.Command <= WorkbenchCommand.OPEN_SUBTITLE_DETAILS)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.EXIT when occupied => binding with { Gesture = "Escape" },
                WorkbenchCommand.SAVE_PROJECT => binding with { Gesture = string.Empty },
                WorkbenchCommand.OPEN_PROJECT => binding with { Gesture = "CmdOrCtrl+Alt+O" },
                _ => binding
            }).ToImmutableArray();
        var upgraded = WorkbenchPreferencesMigration.Upgrade(new() { ShortcutBindings = previous });
        upgraded.Validate();
        var previousCommands = previous.Select(binding => binding.Command).ToHashSet();
        Assert.Equal(previous.AsEnumerable(), upgraded.ShortcutBindings.Where(binding => previousCommands.Contains(binding.Command)));
        var additions = upgraded.ShortcutBindings.Where(binding => !previousCommands.Contains(binding.Command)).ToArray();
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length - previous.Length, additions.Length);
        foreach (var addition in additions.Where(binding => binding.Command != exit))
        {
            Assert.Equal(ShortcutDefaults.CreateBindings().Single(binding => binding.Command == addition.Command), addition);
        }
        Assert.Equal(occupied ? string.Empty : "Escape", Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == exit).Gesture);
        Assert.Equal(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded));
    }
}
