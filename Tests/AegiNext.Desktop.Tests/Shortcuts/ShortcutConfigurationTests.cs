using AegiNext.Desktop.Shortcuts;
using Avalonia.Input;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class ShortcutConfigurationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DefaultsCoverEveryCommandAndHaveNoPlatformConflicts(bool mac)
    {
        var bindings = ShortcutDefaults.CreateBindings();
        ShortcutConfiguration.Validate(bindings, mac);
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length, bindings.Length);
        Assert.Equal(bindings.Length, bindings.Select(binding => binding.Command).Distinct().Count());
    }

    [Theory]
    [InlineData("S+T")]
    [InlineData("Hyper+S")]
    [InlineData("Control+Control+S")]
    [InlineData("Ctrl+Control+S")]
    [InlineData("None")]
    [InlineData("LeftCtrl")]
    [InlineData("999")]
    [InlineData("44")]
    [InlineData("Control+")]
    [InlineData("Control+NoKey")]
    [InlineData("Ctrl++S")]
    [InlineData("F8\n")]
    public void MalformedOrAmbiguousGestureIsRejected(string gesture)
    {
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.NormalizeGesture(gesture, false));
    }

    [Theory]
    [InlineData(true, "Meta+S", "CmdOrCtrl+S")]
    [InlineData(true, "Cmd+S", "CmdOrCtrl+S")]
    [InlineData(false, "ctrl+s", "CmdOrCtrl+S")]
    [InlineData(false, "Ctrl+S", "CmdOrCtrl+S")]
    [InlineData(true, "Control+S", "Control+S")]
    [InlineData(false, "Meta+S", "Meta+S")]
    [InlineData(false, "Shift+Control+S", "CmdOrCtrl+Shift+S")]
    [InlineData(false, "Ctrl++", "CmdOrCtrl+OemPlus")]
    [InlineData(false, "Control+Return", "CmdOrCtrl+Enter")]
    [InlineData(true, "  ", "")]
    public void NormalizationPreservesPortableAndPhysicalModifiers(bool mac, string input, string expected)
    {
        Assert.Equal(expected, ShortcutConfiguration.NormalizeGesture(input, mac));
    }

    [Theory]
    [InlineData(true, "CmdOrCtrl+S", "Meta+S")]
    [InlineData(false, "CmdOrCtrl+S", "Control+S")]
    [InlineData(false, "Ctrl+Shift+S", "Shift+Control+S")]
    [InlineData(true, "F8", "F8")]
    public void AliasesAndModifierOrderCannotHideConflicts(bool mac, string first, string second)
    {
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.Validate(
            [new(WorkbenchCommand.SAVE_PROJECT, first), new(WorkbenchCommand.OPEN_PROJECT, second)], mac));
    }

    [Theory]
    [InlineData(true, "CmdOrCtrl+Meta+S")]
    [InlineData(false, "CmdOrCtrl+Control+S")]
    public void PortableAndPhysicalDuplicateModifierIsInvalid(bool mac, string gesture)
    {
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.NormalizeGesture(gesture, mac));
    }

    [Fact]
    public void EmptyBindingsAreDisabledButDuplicateOrUnknownCommandsAreInvalid()
    {
        ShortcutConfiguration.Validate([new(WorkbenchCommand.SAVE_PROJECT, ""), new(WorkbenchCommand.OPEN_PROJECT, "")]);
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.Validate(
            [new(WorkbenchCommand.SAVE_PROJECT, ""), new(WorkbenchCommand.SAVE_PROJECT, "F8")]));
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.Validate([new((WorkbenchCommand)999, "F8")]));
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.Validate([new(WorkbenchCommand.SAVE_PROJECT, null!)]));
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.Validate([null!]));
    }

    [Theory]
    [InlineData(true, KeyModifiers.Meta | KeyModifiers.Control | KeyModifiers.Shift)]
    [InlineData(false, KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Shift)]
    public void CapturedGestureRoundTripsExactPhysicalModifiers(bool mac, KeyModifiers modifiers)
    {
        var gesture = ShortcutConfiguration.FormatGesture(Key.F8, modifiers, mac);
        var router = new ShortcutRouter([new(WorkbenchCommand.TIMING_ENTER, gesture)], mac);
        Assert.True(router.TryResolve(Key.F8, modifiers, true, out var command));
        Assert.Equal(WorkbenchCommand.TIMING_ENTER, command);
        Assert.False(router.TryResolve(Key.F8, KeyModifiers.None, false, out _));
    }

    [Fact]
    public void CapturingModifierOrUndefinedKeyCannotCreateBinding()
    {
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.FormatGesture(Key.LeftCtrl, KeyModifiers.Control));
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.FormatGesture((Key)999, KeyModifiers.None));
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.FormatGesture(Key.S, (KeyModifiers)1024));
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.NormalizeGesture(new('A', 129)));
    }

    [Fact]
    public void ConfiguringAConflictCannotMutateAnExistingRouter()
    {
        var router = new ShortcutRouter(ShortcutDefaults.CreateBindings(), false);
        Assert.Throws<InvalidDataException>(() => new ShortcutRouter(
            [new(WorkbenchCommand.SAVE_PROJECT, "Control+S"), new(WorkbenchCommand.OPEN_PROJECT, "Ctrl+S")], false));
        Assert.True(router.TryResolve(Key.S, KeyModifiers.Control, false, out var command));
        Assert.Equal(WorkbenchCommand.SAVE_PROJECT, command);
    }

    [Fact]
    public void SettingRowRetainsDraftAndOnlyNotifiesChangedValues()
    {
        var row = new ShortcutSettingRow(WorkbenchCommand.SAVE_PROJECT, "保存", "Ctrl+S");
        var changes = new List<string?>();
        row.PropertyChanged += (_, change) => changes.Add(change.PropertyName);
        var binding = row.ToBinding(false);
        Assert.Equal("CmdOrCtrl+S", binding.Gesture);
        Assert.Equal("Ctrl+S", row.Gesture);
        row.Gesture = row.Gesture;
        Assert.Empty(changes);
        row.Gesture = "S+T";
        Assert.Equal(nameof(ShortcutSettingRow.Gesture), Assert.Single(changes));
        Assert.Throws<InvalidDataException>(() => row.ToBinding(false));
        Assert.Equal("CmdOrCtrl+S", binding.Gesture);
    }
}
