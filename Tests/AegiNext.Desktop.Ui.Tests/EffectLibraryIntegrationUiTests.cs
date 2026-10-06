using AegiNext.Core.Projects;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectLibraryIntegrationUiTests
{
    [AvaloniaFact]
    public async Task RealSettingsSaveUpdatesPersonalLibraryAndPanelWithoutEditingProjectThenAppliesAtClipLength()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        UiTestActions.CreateSubtitle(context);
        await context.Session.EffectScripts.Completion;
        var original = context.Session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.EFFECTS);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        settings.ViewModel.Effects.Name = "Personal timing script";
        var source = settings.ViewModel.Effects.Source;
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await context.Session.EffectScripts.Completion;
        Dispatcher.UIThread.RunJobs();
        var preset = Assert.Single(context.Session.EffectScriptLibrary.Snapshot.Presets);
        Assert.Equal(source, preset.Source);
        Assert.Equal("Personal timing script", preset.Name);
        Assert.False(settings.ViewModel.Effects.IsDirty);
        Assert.Same(original, context.Session.DocumentSnapshot);
        var combo = UiTestActions.Find<ComboBox>(context.Window, "PresetCombo");
        combo.SelectedIndex = Array.IndexOf(context.ViewModel.Effects.Presets, preset.Name);
        Assert.True(combo.SelectedIndex >= 7);
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        var layer = context.Session.SelectedLayer!;
        var track = Assert.Single(layer.Tracks);
        Assert.Equal(AnimationProperty.OPACITY, track.Property);
        Assert.Equal(layer.End - layer.Start + layer.AnimationOffset, track.Keyframes[^1].Time);
        context.Session.Editor.Undo();
        Assert.Same(original, context.Session.DocumentSnapshot);
        settings.Close();
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
        var reopened = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        reopened.SelectPage(SettingsPage.EFFECTS);
        Assert.Contains(reopened.ViewModel.Effects.Effects, item => item.Id == preset.Id);
    }

    [AvaloniaFact]
    public async Task InvalidTemplateCannotSaveOrModifyProjectAndSavedTemplateIsAppliedFromWorkbench()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        UiTestActions.CreateSubtitle(context);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SETTINGS);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        settings.SelectPage(SettingsPage.EFFECTS);
        UiTestActions.Click(settings, "AddEffectScriptButton");
        var valid = settings.ViewModel.Effects.Source;
        var original = context.Session.DocumentSnapshot;
        settings.ViewModel.Effects.Source = "effect broken";
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await context.Session.EffectScripts.Completion;
        Assert.NotNull(settings.ViewModel.Effects.Error);
        Assert.Empty(context.Session.EffectScriptLibrary.Snapshot.Presets);
        Assert.Same(original, context.Session.DocumentSnapshot);
        settings.ViewModel.Effects.Source = valid;
        UiTestActions.Click(settings, "SaveEffectScriptButton");
        await context.Session.EffectScripts.Completion;
        Assert.Same(original, context.Session.DocumentSnapshot);
        var preset = Assert.Single(context.Session.EffectScriptLibrary.Snapshot.Presets);
        var combo = UiTestActions.Find<ComboBox>(context.Window, "PresetCombo");
        combo.SelectedIndex = Array.IndexOf(context.ViewModel.Effects.Presets, preset.Name);
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        Assert.Single(context.Session.SelectedLayer!.Tracks);
        Assert.False(settings.ViewModel.Effects.IsDirty);
    }
}
