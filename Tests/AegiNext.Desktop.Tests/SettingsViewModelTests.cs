using System.Security.Cryptography;
using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Appearance;
using AegiNext.Desktop.Settings.Colors;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Settings.Styles;
using AegiNext.Desktop.Settings.Preview;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

public sealed class SettingsViewModelTests
{
    [Fact]
    public void AppearanceMenuLocationEmitsImmediateSemanticChangeAndReloadDoesNotEmit()
    {
        var model = new AppearanceSettingsViewModel(new() { Language = "en-US" }, true);
        var updates = new List<SettingsAppearanceChangedEventArgs>();
        model.Changed += (_, value) => updates.Add(value);

        model.MenuLocationIndex = 1;

        Assert.True(model.ShowMenuLocation);
        Assert.True(Assert.Single(updates).WindowMenuOnMac);
        Assert.Equal("en-US", updates[0].Language);
        model.UpdatePreferences(new() { WindowMenuOnMac = true, Language = "zh-CN", Theme = WorkbenchTheme.DARK });
        model.RefreshLanguage();
        Assert.Single(updates);
        Assert.Equal(1, model.MenuLocationIndex);
        Assert.Equal("zh-CN", model.SelectedLanguage!.LanguageID);
        Assert.Equal(2, model.ThemeIndex);
    }

    [Fact]
    public void AppearanceLanguageRefreshRestoresIdentifierAfterSelectorSynchronizesItsFallback()
    {
        var model = new AppearanceSettingsViewModel(new() { Language = "zh-CN" });
        var changes = new List<SettingsAppearanceChangedEventArgs>();
        model.Changed += (_, change) => changes.Add(change);
        model.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(AppearanceSettingsViewModel.Languages))
            {
                model.SelectedLanguage = model.Languages.Single(language => language.LanguageID == "en-US");
            }
        };

        model.RefreshLanguage();

        Assert.Equal("zh-CN", model.SelectedLanguage!.LanguageID);
        Assert.Empty(changes);
        model.ThemeIndex = (int)WorkbenchTheme.DARK;
        Assert.Equal("zh-CN", Assert.Single(changes).Language);
    }

    [Fact]
    public void InvalidShortcutDraftSurvivesNavigationAndLanguageUntilCorrected()
    {
        var model = new SettingsWindowViewModel(new());
        var command = model.Shortcuts.SelectedRow!.Command;
        model.PageIndex = (int)SettingsPage.SHORTCUTS;
        model.Shortcuts.Gesture = "Control+";
        model.Shortcuts.IsRecording = true;

        model.PageIndex = (int)SettingsPage.APPEARANCE;
        model.RefreshLanguage();
        model.PageIndex = (int)SettingsPage.SHORTCUTS;

        Assert.False(model.Shortcuts.IsRecording);
        Assert.Equal(command, model.Shortcuts.SelectedRow!.Command);
        Assert.Equal("Control+", model.Shortcuts.Gesture);
        Assert.True(model.HasError);
        Assert.NotNull(model.Shortcuts.Error);
        var saved = new List<SettingsShortcutsChangedEventArgs>();
        model.Shortcuts.Changed += (_, value) => saved.Add(value);
        model.Shortcuts.CaptureGesture("F6");
        Assert.False(model.HasError);
        Assert.Equal("F6", Assert.Single(saved).Bindings.Single(value => value.Command == command).Gesture);
    }

    [Fact]
    public void StyleValidationKeepsInvalidNameAndNumericDraftWithoutSendingRequests()
    {
        var model = new StyleSettingsViewModel();
        model.UpdateStyles([new(Guid.NewGuid(), "Original", new())]);
        var requests = new List<SubtitleStylePreset>();
        model.UpsertRequested += (_, value) => requests.Add(value.Preset);
        model.Name = "  ";
        model.FontSize = null;
        model.SaveCommand.Execute(null);

        Assert.Empty(requests);
        Assert.Equal("  ", model.Name);
        Assert.Null(model.FontSize);
        Assert.NotNull(model.Error);
        model.RefreshLanguage();
        Assert.Equal("  ", model.Name);
        model.Name = "Updated";
        model.SaveCommand.Execute(null);
        Assert.Empty(requests);
        model.FontSize = 72;
        model.SaveCommand.Execute(null);
        Assert.Equal(72, Assert.Single(requests).Style.FontSize);
        Assert.Null(model.Error);
    }

    [Theory]
    [InlineData("FontSizeText", "FontSizeInput", "72.5")]
    [InlineData("StrokeWidthText", "StrokeWidthInput", "2.5")]
    [InlineData("MarginText", "MarginInput", "48")]
    [InlineData("LineHeightText", "LineHeightInput", "1.5")]
    [InlineData("ShadowBlurText", "ShadowBlurInput", "2.5")]
    [InlineData("ShadowXText", "ShadowXInput", "-4")]
    [InlineData("ShadowYText", "ShadowYInput", "4")]
    public void UnparsedStyleNumbersRemainRawAndPreventSaveAndApply(string propertyName, string fieldKey,
        string validText)
    {
        var model = new StyleSettingsViewModel { HasSelectedSubtitle = true };
        model.UpdateStyles([new(Guid.NewGuid(), "Original", new())]);
        var property = typeof(StyleSettingsViewModel).GetProperty(propertyName)!;
        var saved = new List<SubtitleStylePreset>();
        var applied = new List<SubtitleStylePreset>();
        model.UpsertRequested += (_, value) => saved.Add(value.Preset);
        model.ApplyRequested += (_, value) => applied.Add(value.Preset);
        property.SetValue(model, "7e-");

        model.SaveCommand.Execute(null);
        model.ApplyCommand.Execute(null);
        model.RefreshLanguage();

        Assert.Empty(saved);
        Assert.Empty(applied);
        Assert.Equal("7e-", property.GetValue(model));
        Assert.Equal(fieldKey, model.InvalidFieldKey);
        Assert.NotNull(model.Error);
        property.SetValue(model, validText);
        model.SaveCommand.Execute(null);
        model.ApplyCommand.Execute(null);
        Assert.Equal(Assert.Single(saved), Assert.Single(applied));
        Assert.Null(model.Error);
        Assert.Null(model.InvalidFieldKey);
    }

    [Theory]
    [InlineData("FontSizeText", "0")]
    [InlineData("StrokeWidthText", "-1")]
    [InlineData("MarginText", "32769")]
    [InlineData("LineHeightText", "10.1")]
    [InlineData("ShadowBlurText", "513")]
    [InlineData("ShadowXText", "-1000000001")]
    [InlineData("ShadowYText", "1000000001")]
    public void OutOfRangeStyleNumbersRemainRawAndDoNotSubmit(string propertyName, string invalidText)
    {
        var model = new StyleSettingsViewModel();
        model.UpdateStyles([new(Guid.NewGuid(), "Original", new())]);
        var requests = new List<SubtitleStylePreset>();
        model.UpsertRequested += (_, value) => requests.Add(value.Preset);
        var property = typeof(StyleSettingsViewModel).GetProperty(propertyName)!;
        property.SetValue(model, invalidText);

        model.SaveCommand.Execute(null);

        Assert.Empty(requests);
        Assert.Equal(invalidText, property.GetValue(model));
        Assert.NotNull(model.InvalidFieldKey);
        Assert.NotNull(model.Error);
    }

    [Fact]
    public void TypographyChangesPreserveHdrAndPortableFontAndExplicitFontConfirmationReplacesFont()
    {
        var font = new EmbeddedSubtitleFont("sample.ttf", Convert.ToHexStringLower(SHA256.HashData([1, 2, 3])),
            [1, 2, 3]);
        var original =
            new SubtitleStylePreset(Guid.NewGuid(), "Original", new() { Fill = new(2.5, 1.25, 0.5, 0.9) }, font);
        var model = new StyleSettingsViewModel();
        model.UpdateStyles([original]);
        model.Bold = true;
        model.Margin = 88;
        model.RefreshLanguage();

        Assert.Same(font, model.Draft!.Font);
        Assert.Equal(original.Style.Fill, model.Draft.Style.Fill);
        Assert.True(model.Draft.Style.Bold);
        Assert.False(original.Style.Bold);
        model.DuplicateCommand.Execute(null);
        Assert.NotEqual(original.Id, model.Draft.Id);
        Assert.Same(font, model.Draft.Font);
        model.CommitFont("serif");
        Assert.Null(model.Draft.Font);
    }

    [Fact]
    public void BusyAndSelectionAvailabilityControlAllStyleCommands()
    {
        var model = new StyleSettingsViewModel();
        model.UpdateStyles([new(Guid.NewGuid(), "Original", new())]);
        Assert.False(model.ApplyCommand.CanExecute(null));
        Assert.False(model.CaptureCommand.CanExecute(null));
        model.HasSelectedSubtitle = true;
        Assert.True(model.ApplyCommand.CanExecute(null));
        model.IsBusy = true;
        Assert.False(model.CanEdit);
        Assert.All(
            new[]
            {
                model.AddCommand, model.DuplicateCommand, model.DeleteCommand, model.SaveCommand, model.ApplyCommand,
                model.CaptureCommand, model.ImportCommand, model.ExportCommand
            }, command => Assert.False(command.CanExecute(null)));
        model.IsBusy = false;
        Assert.True(model.CanApply);
    }

    [Fact]
    public void PageModelsDoNotExposeControlsWindowsOrDockObjects()
    {
        foreach (var type in new[]
                 {
                     typeof(SettingsWindowViewModel), typeof(AppearanceSettingsViewModel),
                     typeof(ColorsSettingsViewModel),
                     typeof(PreviewSettingsViewModel),
                     typeof(ShortcutSettingsViewModel), typeof(StyleSettingsViewModel)
                 })
        {
            var members = type.GetProperties().Select(value => value.PropertyType)
                .Concat(type
                    .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                    .Select(value => value.FieldType));
            Assert.All(members, member =>
            {
                Assert.False(typeof(Avalonia.Controls.Control).IsAssignableFrom(member));
                Assert.False(member.Namespace?.StartsWith("Dock.", StringComparison.Ordinal) == true);
            });
        }
    }
}
