using AegiNext.Core.Presets;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.TimingPostProcessor;

namespace AegiNext.Desktop.Tests;

public sealed class TimingPostProcessorSettingsTests
{
    [Fact]
    public async Task ExistingPreferencesLoadOriginalTimingDefaultsWithoutRewritingTheFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Language\":\"zh-CN\",\"Volume\":0.375}";
        await File.WriteAllTextAsync(path, JSON, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(new TimingPostProcessorOptions(), preferences.TimingPostProcessor.Options);
        Assert.Equal(0.375f, preferences.Volume);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task AllTimingSettingsPersistAndParticipateInPreferenceEquality()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences
        {
            Language = "zh-CN", Volume = 0.375f, Theme = WorkbenchTheme.DARK,
            TimingPostProcessor = new()
            {
                Options = new()
                {
                    LeadInEnabled = false, LeadInMilliseconds = 120,
                    LeadOutEnabled = false, LeadOutMilliseconds = 121,
                    AdjacencyEnabled = false, MaximumGapMilliseconds = 301, MaximumOverlapMilliseconds = 51,
                    BiasPercent = 55, KeyframeSnapEnabled = false,
                    StartBeforeMilliseconds = 201, StartAfterMilliseconds = 151,
                    EndBeforeMilliseconds = 202, EndAfterMilliseconds = 251
                }
            }
        };

        await store.SaveAsync(expected, CancellationToken.None);

        using var reopened = new WorkbenchPreferencesStore(directory.Path);
        var actual = reopened.Load();
        Assert.Null(reopened.LoadError);
        Assert.Equal(expected, actual);
        Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
        Assert.NotEqual(expected, expected with
        {
            TimingPostProcessor = expected.TimingPostProcessor with
            {
                Options = expected.TimingPostProcessor.Options with { EndAfterMilliseconds = 252 }
            }
        });
    }

    [Fact]
    public async Task InvalidTimingPreferenceCannotReplaceTheSavedPreferences()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var original = new WorkbenchPreferences { Language = "zh-CN", Volume = 0.375f };
        await store.SaveAsync(original, CancellationToken.None);
        var path = Path.Combine(directory.Path, "preferences.json");
        var bytes = await File.ReadAllBytesAsync(path, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(original with
        {
            TimingPostProcessor = original.TimingPostProcessor with
            {
                Options = original.TimingPostProcessor.Options with { MaximumOverlapMilliseconds = -1 }
            }
        }, CancellationToken.None));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, CancellationToken.None));
        Assert.Equal(original, store.Load());
    }

    [Theory]
    [InlineData("")]
    [InlineData("-1")]
    [InlineData("120.")]
    [InlineData("120.5")]
    [InlineData("1e3")]
    [InlineData("2147483648")]
    public void InvalidDraftSurvivesExternalPreferencesAndLanguageUntilOnlyThatFieldIsRestored(string text)
    {
        var original = new WorkbenchPreferences();
        var model = new TimingPostProcessorSettingsViewModel(original);
        var notifications = 0;
        model.Changed += (_, _) => notifications++;
        model.LeadInMillisecondsText = text;
        model.LeadOutMillisecondsText = "bad";

        Assert.False(model.Commit(TimingPostProcessorField.LEAD_IN));
        model.UpdatePreferences(original with
        {
            Theme = WorkbenchTheme.DARK,
            TimingPostProcessor = original.TimingPostProcessor with
            {
                Options = original.TimingPostProcessor.Options with { LeadInMilliseconds = 750 }
            }
        });
        model.RefreshLanguage();

        Assert.Equal(text, model.LeadInMillisecondsText);
        Assert.Equal(750, model.Options.LeadInMilliseconds);
        Assert.NotNull(model.Error);
        Assert.Equal(0, notifications);
        model.Restore(TimingPostProcessorField.LEAD_IN);
        Assert.Equal("750", model.LeadInMillisecondsText);
        Assert.Equal("bad", model.LeadOutMillisecondsText);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void ValidFieldCommitsOnceAndPreservesOtherUnconfirmedFields()
    {
        var model = new TimingPostProcessorSettingsViewModel(new());
        var changes = new List<TimingPostProcessorPreferences>();
        model.Changed += (_, change) => changes.Add(change.Preferences);
        model.LeadInMillisecondsText = "0";
        model.MaximumGapMillisecondsText = "invalid";

        Assert.True(model.Commit(TimingPostProcessorField.LEAD_IN));
        Assert.True(model.Commit(TimingPostProcessorField.LEAD_IN));

        Assert.Equal(0, Assert.Single(changes).Options.LeadInMilliseconds);
        Assert.Equal("invalid", model.MaximumGapMillisecondsText);
        Assert.False(model.CommitAll());
        Assert.NotNull(model.Error);
        Assert.Single(changes);
    }

    [Fact]
    public void CustomPresetIdentitySurvivesRenameAndDeletedPresetsLeaveTheList()
    {
        var model = new TimingPostProcessorSettingsViewModel(new());
        var first = new SubtitleStylePreset(Guid.NewGuid(), "Default", new());
        var custom = new SubtitleStylePreset(Guid.NewGuid(), "对白 中文 ABC 123", new());
        model.UpdateStyles([first, custom]);
        Assert.All(model.Styles, style => Assert.False(style.IsSelected));
        var selected = model.Styles.Single(style => style.Id == custom.Id);
        selected.IsSelected = true;
        model.SelectedStyle = selected;
        model.UpdateStyles([first, custom with { Name = "Renamed 自定义 123" }]);

        Assert.True(model.Styles.Single(style => style.Id == custom.Id).IsSelected);
        Assert.Equal(custom.Id, model.SelectedStyle!.Id);
        Assert.Equal("Renamed 自定义 123", model.SelectedStyle.Name);
        Assert.False(model.Styles.Single(style => style.Id == first.Id).IsSelected);
        model.UpdateStyles([first]);
        Assert.DoesNotContain(model.Styles, style => style.Id == custom.Id);
        Assert.False(model.AssociateCommand.CanExecute(null));
    }

    [Fact]
    public void AssociationUsesSelectedPresetIdsAndBusyStateBlocksRepeatedRequests()
    {
        var model = new TimingPostProcessorSettingsViewModel(new());
        var first = new SubtitleStylePreset(Guid.NewGuid(), "Default", new());
        var custom = new SubtitleStylePreset(Guid.NewGuid(), "对白 中文 ABC 123", new());
        model.UpdateStyles([first, custom]);
        model.Styles.Single(style => style.Id == custom.Id).IsSelected = true;
        var requests = new List<(TimingPostProcessorOptions Options, IReadOnlySet<Guid> Ids)>();
        model.AssociateRequested += (_, change) => requests.Add((change.Options!, change.StyleIds));

        model.AssociateCommand.Execute(null);

        var request = Assert.Single(requests);
        Assert.Equal(custom.Id, Assert.Single(request.Ids));
        Assert.Equal(model.Options, request.Options);
        model.IsBusy = true;
        Assert.False(model.AssociateCommand.CanExecute(null));
        Assert.False(model.UnlinkCommand.CanExecute(null));
        model.IsBusy = false;
        model.SelectNoneCommand.Execute(null);
        Assert.False(model.AssociateCommand.CanExecute(null));
        model.SelectAllCommand.Execute(null);
        Assert.True(model.AssociateCommand.CanExecute(null));
    }

    [Fact]
    public void SelectingAssociatedPresetLoadsItsOwnOptionsAndExposesTheAssociation()
    {
        var model = new TimingPostProcessorSettingsViewModel(new());
        var unlinked = new SubtitleStylePreset(Guid.NewGuid(), "Other", new());
        var options = new TimingPostProcessorOptions { LeadInMilliseconds = 750, EndAfterMilliseconds = 333 };
        var linked = new SubtitleStylePreset(Guid.NewGuid(), "Custom 中文 ABC 123", new())
        {
            TimingPostProcessor = options
        };
        model.UpdateStyles([unlinked, linked]);
        var row = model.Styles.Single(style => style.Id == linked.Id);
        model.SelectedStyle = row;

        Assert.True(row.IsAssociated);
        Assert.True(row.IsSelected);
        Assert.False(model.Styles.Single(style => style.Id == unlinked.Id).IsAssociated);
        Assert.Equal(options, model.Options);
        Assert.Equal("750", model.LeadInMillisecondsText);
        Assert.Equal("333", model.EndAfterMillisecondsText);
        Assert.True(model.UnlinkCommand.CanExecute(null));
    }

    [Fact]
    public void SelectedAssociatedPresetFollowsExternalParameterChangesWhenTheCandidateHasNoLocalEdits()
    {
        var model = new TimingPostProcessorSettingsViewModel(new());
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Custom", new())
        {
            TimingPostProcessor = new() { LeadInMilliseconds = 500, EndAfterMilliseconds = 333 }
        };
        model.UpdateStyles([original]);
        model.SelectedStyle = Assert.Single(model.Styles);
        var updated = original with
        {
            TimingPostProcessor = original.TimingPostProcessor! with { LeadInMilliseconds = 800, EndAfterMilliseconds = 444 }
        };

        model.UpdateStyles([updated]);

        Assert.Equal(original.Id, model.SelectedStyle!.Id);
        Assert.Equal(updated.TimingPostProcessor, model.Options);
        Assert.Equal("800", model.LeadInMillisecondsText);
        Assert.Equal("444", model.EndAfterMillisecondsText);
        Assert.Null(model.Error);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("invalid")]
    [InlineData("confirmed")]
    public void ExternalAssociationRefreshPreservesUnconfirmedInvalidAndConfirmedLocalCandidateEdits(string edit)
    {
        var model = new TimingPostProcessorSettingsViewModel(new());
        var original = new SubtitleStylePreset(Guid.NewGuid(), "Custom", new())
        {
            TimingPostProcessor = new() { LeadInMilliseconds = 500 }
        };
        model.UpdateStyles([original]);
        model.SelectedStyle = Assert.Single(model.Styles);
        model.LeadInMillisecondsText = edit == "invalid" ? "invalid draft" : "900";
        if (edit != "pending")
        {
            Assert.Equal(edit == "confirmed", model.Commit(TimingPostProcessorField.LEAD_IN));
        }

        var candidate = model.Options;
        var rawText = model.LeadInMillisecondsText;
        var error = model.Error;
        var updated = original with { TimingPostProcessor = original.TimingPostProcessor! with { LeadInMilliseconds = 800 } };

        model.UpdateStyles([updated]);

        Assert.Equal(original.Id, model.SelectedStyle!.Id);
        Assert.Equal(updated.TimingPostProcessor, model.SelectedStyle.Options);
        Assert.Equal(candidate, model.Options);
        Assert.Equal(rawText, model.LeadInMillisecondsText);
        Assert.Equal(error, model.Error);
    }

    [Fact]
    public void NavigationKeepsTheTimingDraftAndShowsItsErrorOnlyOnThatPage()
    {
        var model = new SettingsWindowViewModel(new());
        model.PageIndex = (int)SettingsPage.TIMING_POST_PROCESSOR;
        model.TimingPostProcessor.LeadOutMillisecondsText = "invalid";
        Assert.False(model.TimingPostProcessor.Commit(TimingPostProcessorField.LEAD_OUT));

        model.PageIndex = (int)SettingsPage.APPEARANCE;
        Assert.False(model.HasError);
        model.RefreshLanguage();
        model.PageIndex = (int)SettingsPage.TIMING_POST_PROCESSOR;

        Assert.True(model.HasError);
        Assert.Equal("invalid", model.TimingPostProcessor.LeadOutMillisecondsText);
    }
}
