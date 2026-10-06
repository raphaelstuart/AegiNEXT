using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class ShortcutSettingsAutoSaveTests
{
    [Fact]
    public void ValidEditsPublishCompleteBindingsOnceWithoutSubmittingNavigationOrRefresh()
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        var changes = new List<SettingsShortcutsChangedEventArgs>();
        model.Changed += (_, change) => changes.Add(change);

        model.CaptureGesture("F6");

        var bindings = Assert.Single(changes).Bindings;
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length, bindings.Length);
        Assert.Equal("F6", bindings.Single(binding => binding.Command == WorkbenchCommand.NEW_PROJECT).Gesture);
        model.CaptureGesture("F6");
        model.SelectedRow = model.Rows.Single(row => row.Command == WorkbenchCommand.SAVE_PROJECT);
        model.RefreshLanguage();
        Assert.Single(changes);

        model.ClearCommand.Execute(null);

        Assert.Equal(2, changes.Count);
        Assert.Equal(string.Empty, changes[^1].Bindings.Single(binding => binding.Command == WorkbenchCommand.SAVE_PROJECT).Gesture);
    }

    [Fact]
    public void InvalidOrConflictingDraftsNeverPublishUntilTheCompleteConfigurationIsValid()
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        var changes = new List<SettingsShortcutsChangedEventArgs>();
        model.Changed += (_, change) => changes.Add(change);
        model.CaptureGesture("F6");
        Assert.Single(changes);
        model.SelectedRow = model.Rows.Single(row => row.Command == WorkbenchCommand.SAVE_PROJECT);

        model.CaptureGesture("F6");
        Assert.NotNull(model.Error);
        model.Gesture = "Control+";
        model.RefreshLanguage();
        Assert.Equal("Control+", model.Gesture);
        Assert.Single(changes);

        model.CaptureGesture("F7");

        Assert.Null(model.Error);
        Assert.Equal(2, changes.Count);
        Assert.Equal("F6", changes[^1].Bindings.Single(binding => binding.Command == WorkbenchCommand.NEW_PROJECT).Gesture);
        Assert.Equal("F7", changes[^1].Bindings.Single(binding => binding.Command == WorkbenchCommand.SAVE_PROJECT).Gesture);
    }

    [Fact]
    public void SynchronousConfigurationFeedbackAndExternalReloadNeverRepublish()
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        var changes = new List<SettingsShortcutsChangedEventArgs>();
        model.Changed += (_, change) =>
        {
            changes.Add(change);
            model.RefreshLanguage();
            model.UpdateBindings(change.Bindings);
        };

        model.CaptureGesture("F6");

        Assert.Single(changes);
        Assert.Equal("F6", model.Gesture);
        model.UpdateBindings(ShortcutDefaults.CreateBindings());
        model.CaptureGesture("CmdOrCtrl+N");
        Assert.Single(changes);
    }

    [Fact]
    public void ResetDiscardsInvalidDraftAndPublishesDefaultsOnlyWhenCommittedBindingsDiffer()
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        var changes = new List<SettingsShortcutsChangedEventArgs>();
        model.Changed += (_, change) => changes.Add(change);
        model.CaptureGesture("F6");
        model.Gesture = "Control+";

        model.ResetCommand.Execute(null);

        Assert.Null(model.Error);
        Assert.Equal(2, changes.Count);
        Assert.Equal<ShortcutBinding>(ShortcutDefaults.CreateBindings(), changes[^1].Bindings);
        model.ResetCommand.Execute(null);
        Assert.Equal(2, changes.Count);
    }
}
