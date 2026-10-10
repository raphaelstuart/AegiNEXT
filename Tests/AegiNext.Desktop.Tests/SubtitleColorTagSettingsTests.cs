using AegiNext.Application.ColorTags;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Colors;
using AegiNext.Desktop.Settings.ColorTags;

namespace AegiNext.Desktop.Tests;

public sealed class SubtitleColorTagSettingsTests
{
    [Fact]
    public async Task SavingValidatesEveryDraftAndRetainsInvalidTextWhenTheSelectionChanges()
    {
        using var model = new SubtitleColorTagsSettingsViewModel();
        var original = Library();
        model.UpdateLibrary(original);
        var saves = 0;
        model.SaveDraftAsync = (value, _) =>
        {
            saves++;
            return Task.FromResult<SubtitleColorTagLibraryDocument?>(value);
        };
        var first = model.SelectedTag!;
        first.ColorDraft.HexText = "#bad";
        model.SelectedTag = model.Tags[1];
        model.SelectedTag.Name = "Finished";

        Assert.False(await model.SavePendingAsync());

        Assert.Equal(0, saves);
        Assert.Same(first, model.SelectedTag);
        Assert.Equal("#bad", first.ColorDraft.HexText);
        Assert.True(model.IsDirty);
        model.DiscardDraft();
        Assert.False(model.IsDirty);
        Assert.Equal(original.Tags.Select(tag => tag.Name), model.Tags.Select(tag => tag.Name));
    }

    [Fact]
    public async Task FailedStoragePreservesTheDraftAndRetryCommitsTheWholeLibrary()
    {
        using var model = new SubtitleColorTagsSettingsViewModel();
        var original = Library();
        model.UpdateLibrary(original);
        model.SelectedTag!.Name = "Renamed";
        model.AddCommand.Execute(null);
        model.SelectedTag!.Name = "New label";
        model.SelectedTag.ColorDraft.HexText = "#12AB34";
        model.SaveDraftAsync = (_, _) => Task.FromResult<SubtitleColorTagLibraryDocument?>(null);

        Assert.False(await model.SavePendingAsync());
        Assert.True(model.IsDirty);
        Assert.Equal("#12AB34", model.SelectedTag.ColorDraft.HexText);
        SubtitleColorTagLibraryDocument? saved = null;
        model.SaveDraftAsync = (value, expected) =>
        {
            Assert.Same(original, expected);
            saved = value;
            return Task.FromResult<SubtitleColorTagLibraryDocument?>(value);
        };

        Assert.True(await model.SavePendingAsync());

        Assert.False(model.IsDirty);
        Assert.Equal(3, saved!.Tags.Length);
        Assert.Equal("Renamed", saved.Tags[0].Name);
        Assert.Equal("#12AB34", saved.Tags[^1].ColorHex);
    }

    [Theory]
    [InlineData(0, true, false)]
    [InlineData(1, true, false)]
    [InlineData(2, false, true)]
    public async Task LeaveHonorsSaveDiscardAndCancel(int choice, bool accepted, bool dirty)
    {
        using var model = new SubtitleColorTagsSettingsViewModel();
        model.UpdateLibrary(Library());
        model.SelectedTag!.Name = "Changed";
        var saves = 0;
        model.SaveDraftAsync = (value, _) =>
        {
            saves++;
            return Task.FromResult<SubtitleColorTagLibraryDocument?>(value);
        };
        model.ConfirmLeaveAsync = () => Task.FromResult(choice);

        Assert.Equal(accepted, await model.PrepareToLeaveAsync());
        Assert.Equal(dirty, model.IsDirty);
        Assert.Equal(choice == 0 ? 1 : 0, saves);
    }

    [Fact]
    public async Task NormalizingAnUnchangedRecordRetainsTheCommittedIdentityForTheNextSave()
    {
        using var model = new SubtitleColorTagsSettingsViewModel();
        var original = Library();
        model.UpdateLibrary(original);
        var committed = original;
        model.SaveDraftAsync = (value, expected) =>
        {
            Assert.Same(committed, expected);
            if (!value.Tags.SequenceEqual(committed.Tags))
            {
                committed = value;
            }

            return Task.FromResult<SubtitleColorTagLibraryDocument?>(committed);
        };
        model.SelectedTag!.Name += " ";

        Assert.True(await model.SavePendingAsync());
        Assert.Same(original, committed);
        model.SelectedTag!.Name = "Changed";
        Assert.True(await model.SavePendingAsync());
        model.SelectedTag!.Name = "Changed again";
        Assert.True(await model.SavePendingAsync());
    }

    [Fact]
    public void ThemeLanguageAndResetColorsNeverOverwriteThePersonalLabelDraft()
    {
        var settings = new SettingsWindowViewModel(new());
        var colors = settings.Colors;
        using var model = settings.ColorTags;
        model.UpdateLibrary(Library());
        var selected = model.SelectedTag!;
        selected.Name = "Unsaved";
        selected.ColorDraft.HexText = "#bad";

        colors.UpdatePreferences(new() { Theme = WorkbenchTheme.DARK, AccentColor = "#112233" });
        settings.RefreshLanguage();
        colors.ResetColorsCommand.Execute(null);

        Assert.Same(selected, model.SelectedTag);
        Assert.Equal("Unsaved", selected.Name);
        Assert.Equal("#bad", selected.ColorDraft.HexText);
        Assert.True(model.IsDirty);
    }

    private static SubtitleColorTagLibraryDocument Library() => new()
    {
        Tags =
        [
            new SubtitleColorTag { Name = "Review", ColorHex = "#FF3344" },
            new SubtitleColorTag { Name = "Ready", ColorHex = "#44AA66" }
        ]
    };
}
