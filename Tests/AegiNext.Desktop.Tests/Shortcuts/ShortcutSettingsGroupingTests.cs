using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class ShortcutSettingsGroupingTests
{
    [Fact]
    public void GroupedListContainsEveryCommandOnceAndKeepsBindingOrderForAutomaticSaving()
    {
        var bindings = ShortcutDefaults.CreateBindings().Reverse().ToArray();
        var model = new ShortcutSettingsViewModel(bindings);
        var headers = model.Items.Where(item => !item.IsCommand).ToArray();
        Assert.Equal(7, headers.Length);
        Assert.All(headers, header => Assert.False(string.IsNullOrWhiteSpace(header.SectionTitle)));
        var catalog = LocalizationCatalog.Load(Path.Combine(AppContext.BaseDirectory, "i18n"));
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            Assert.All(headers, header => Assert.True(catalog.GetLanguage(language).Strings.ContainsKey(header.SectionTitleKey!)));
        }
        var displayedRows = model.Items.Where(item => item.IsCommand).Select(item => item.Row!).ToArray();
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Order(), displayedRows.Select(row => row.Command).Order());
        Assert.Equal(displayedRows.Length, displayedRows.Select(row => row.Command).Distinct().Count());
        Assert.All(displayedRows, row => Assert.Contains(row, model.Rows));
        Assert.Equal(WorkbenchCommand.NEW_PROJECT, model.Items[1].Row!.Command);

        SettingsShortcutsChangedEventArgs? saved = null;
        model.Changed += (_, change) => saved = change;
        model.SelectedRow = model.Rows.Single(row => row.Command == WorkbenchCommand.NEW_PROJECT);
        model.CaptureGesture("F6");
        Assert.NotNull(saved);
        Assert.Equal(bindings.Select(binding => binding.Command), saved.Bindings.Select(binding => binding.Command));
        Assert.Equal("F6", saved.Bindings.Single(binding => binding.Command == WorkbenchCommand.NEW_PROJECT).Gesture);
    }

    [Fact]
    public void HeadersCannotReplaceSelectionAndLanguageRefreshKeepsTheSelectedDraft()
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        var selected = model.Items.Single(item => item.Row?.Command == WorkbenchCommand.AUDITION_SUBTITLE);
        model.SelectedItem = selected;
        model.Gesture = "Control+";
        model.SelectedItem = model.Items.First(item => !item.IsCommand);
        Assert.Same(selected, model.SelectedItem);
        Assert.Equal(WorkbenchCommand.AUDITION_SUBTITLE, model.SelectedRow!.Command);
        Assert.NotNull(model.Error);

        model.RefreshLanguage();
        Assert.Equal(WorkbenchCommand.AUDITION_SUBTITLE, model.SelectedItem!.Row!.Command);
        Assert.Same(model.SelectedRow, model.SelectedItem.Row);
        Assert.Equal("Control+", model.Gesture);
        Assert.NotNull(model.Error);
        model.Gesture = "CmdOrCtrl+S";
        Assert.Contains(Localization.Get("Settings.SAVE_PROJECT"), model.Error);
        model.ClearCommand.Execute(null);
        Assert.Null(model.Error);
    }
}
