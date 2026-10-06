using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class ShortcutSettingsValidationTests
{
    [Fact]
    public void MalformedDraftHasItsOwnErrorAndRecordingTheOriginalBindingClearsIt()
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        model.Gesture = "CmdOrCtrl+N\n";
        Assert.Equal(Localization.Get("Settings.ShortcutFormatValidation"), model.Error);
        model.RefreshLanguage();
        Assert.Equal("CmdOrCtrl+N\n", model.Gesture);
        Assert.Equal(Localization.Get("Settings.ShortcutFormatValidation"), model.Error);
        model.CaptureGesture("CmdOrCtrl+N");
        Assert.Null(model.Error);
    }

    [Fact]
    public void ActualConflictIdentifiesOtherCommandAndOwnBindingDoesNotConflict()
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        Assert.Null(model.Error);
        model.CaptureGesture("CmdOrCtrl+S");
        Assert.Contains(Localization.Get("Settings." + nameof(WorkbenchCommand.SAVE_PROJECT)), model.Error);
        Assert.DoesNotContain(Localization.Get("Settings.ShortcutFormatValidation"), model.Error);
        model.RefreshLanguage();
        Assert.Contains(Localization.Get("Settings." + nameof(WorkbenchCommand.SAVE_PROJECT)), model.Error);
        model.ClearCommand.Execute(null);
        Assert.Null(model.Error);
        model.CaptureGesture("CmdOrCtrl+N");
        Assert.Null(model.Error);
    }

    [Theory]
    [InlineData(true, "Meta+K")]
    [InlineData(false, "Control+K")]
    public void ConflictDescriptionContainsStableCommandsAndNormalizedGesture(bool isMacOs, string physicalGesture)
    {
        var bindings = new ShortcutBinding[]
        {
            new(WorkbenchCommand.NEW_PROJECT, "CmdOrCtrl+K"),
            new(WorkbenchCommand.SAVE_PROJECT, physicalGesture)
        };
        var conflict = Assert.IsType<ShortcutConflict>(ShortcutConfiguration.FindConflict(bindings, isMacOs));
        Assert.Equal("CmdOrCtrl+K", conflict.Gesture);
        Assert.Equal(WorkbenchCommand.NEW_PROJECT, conflict.FirstCommand);
        Assert.Equal(WorkbenchCommand.SAVE_PROJECT, conflict.SecondCommand);
        Assert.Throws<InvalidDataException>(() => ShortcutConfiguration.Validate(bindings, isMacOs));
        Assert.Null(ShortcutConfiguration.FindConflict([bindings[0]], isMacOs));
    }
}
