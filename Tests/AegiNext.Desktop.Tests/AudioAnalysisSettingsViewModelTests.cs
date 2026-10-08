using System.Text.Json;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.AudioAnalysis;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Tests;

public sealed class AudioAnalysisSettingsViewModelTests
{
    [Fact]
    public void LegacyPreferencesReceiveDefaultsAndAudioValuesRoundTripWithEquality()
    {
        var original = JsonSerializer.Deserialize<WorkbenchPreferences>("{\"Version\":1,\"Volume\":0.5,\"MaximumConcurrentTasks\":2}")!;
        original.Validate();
        Assert.Equal(new AudioAnalysisPreferences(), original.AudioAnalysis);
        Assert.Equal(0.5, original.Volume);
        Assert.Equal(2, original.MaximumConcurrentTasks);
        var changed = original with
        {
            AudioAnalysis = new()
            {
                AdvancedMode = true,
                Execution = new() { MaximumWorkers = 1024, MemoryBudgetMiB = 128 },
                Recipe = new() { FftSize = 2048, MinimumDecibels = -100 },
                Display = new() { WaveformGain = 2, SpectrumContrast = 1.5 }
            }
        };
        changed.Validate();
        Assert.NotEqual(original, changed);
        var roundTrip = JsonSerializer.Deserialize<WorkbenchPreferences>(JsonSerializer.Serialize(changed))!;
        roundTrip.Validate();
        Assert.Equal(changed, roundTrip);
        Assert.Equal(changed.GetHashCode(), roundTrip.GetHashCode());
    }

    [Fact]
    public void UntouchedFractionalPreferencesKeepExactValuesWhenConfirmedAndApplied()
    {
        var preferences = new AudioAnalysisPreferences
        {
            Display = new() { WaveformGain = 1.23456789012345 },
            Recipe = new() { MinimumFrequency = 40.1234567890123, MinimumDecibels = -80.1234567890123 }
        };
        var model = new AudioAnalysisSettingsViewModel(new() { AudioAnalysis = preferences });
        var changes = 0;
        model.Changed += (_, _) => changes++;
        Assert.True(model.Commit(AudioAnalysisSettingsField.WAVEFORM_GAIN));
        Assert.True(model.ApplyRecipe());
        Assert.Equal(preferences, model.Preferences);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void OrdinaryFieldsCommitImmediatelyWithoutRequestingARebuild()
    {
        var model = new AudioAnalysisSettingsViewModel(new());
        var changes = new List<AudioAnalysisPreferences>();
        var rebuilds = 0;
        model.Changed += (_, args) => changes.Add(args.Preferences);
        model.RebuildRequested += (_, _) => rebuilds++;
        model.MemoryBudgetText = "128";
        Assert.True(model.Commit(AudioAnalysisSettingsField.MEMORY_BUDGET));
        model.WaveformGainText = "2";
        Assert.True(model.Commit(AudioAnalysisSettingsField.WAVEFORM_GAIN));
        Assert.True(model.Commit(AudioAnalysisSettingsField.WAVEFORM_GAIN));
        model.SegmentSamples = 393216;
        Assert.Equal(3, changes.Count);
        Assert.Equal(128, changes[^1].Execution.MemoryBudgetMiB);
        Assert.Equal(2, changes[^1].Display.WaveformGain);
        Assert.Equal(393216, changes[^1].Execution.SegmentSamples);
        Assert.Equal(0, rebuilds);
    }

    [Fact]
    public void ImportedWorkerCountIsPreservedAndOnlyEffectiveCountIsClamped()
    {
        var model = new AudioAnalysisSettingsViewModel(new()
        {
            AudioAnalysis = new() { Execution = new() { MaximumWorkers = 1024 } }
        });
        var changes = 0;
        model.Changed += (_, _) => changes++;
        Assert.Equal("1024", model.MaximumWorkersText);
        Assert.True(model.Commit(AudioAnalysisSettingsField.MAXIMUM_WORKERS));
        Assert.Equal(1024, model.Preferences.Execution.MaximumWorkers);
        Assert.InRange(model.Preferences.Execution.EffectiveMaximumWorkers, 1, model.MaximumWorkersLimit);
        Assert.Equal(0, changes);
        model.MaximumWorkersText = "1023";
        if (model.MaximumWorkersLimit < 1023)
        {
            Assert.False(model.Commit(AudioAnalysisSettingsField.MAXIMUM_WORKERS));
        }
        model.Restore(AudioAnalysisSettingsField.MAXIMUM_WORKERS);
        model.UseAutomaticWorkers = true;
        Assert.True(model.Commit(AudioAnalysisSettingsField.MAXIMUM_WORKERS));
        Assert.Equal(0, model.Preferences.Execution.MaximumWorkers);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("9")]
    [InlineData("1e-")]
    [InlineData("NaN")]
    [InlineData("")]
    public void InvalidOrdinaryDraftSurvivesRefreshAndRestoresOnlyThatField(string raw)
    {
        var model = new AudioAnalysisSettingsViewModel(new());
        model.WaveformGainText = raw;
        model.MemoryBudgetText = "192";
        Assert.False(model.Commit(AudioAnalysisSettingsField.WAVEFORM_GAIN));
        model.UpdatePreferences(new() { AudioAnalysis = new() { Display = new() { WaveformGain = 3 } } });
        model.RefreshLanguage();
        Assert.Equal(raw, model.WaveformGainText);
        Assert.Equal("192", model.MemoryBudgetText);
        Assert.NotNull(model.Error);
        model.Restore(AudioAnalysisSettingsField.WAVEFORM_GAIN);
        Assert.Equal("3", model.WaveformGainText);
        Assert.Equal("192", model.MemoryBudgetText);
        Assert.Null(model.Error);
    }

    [Fact]
    public void RecipeRequiresApplyAndIdenticalApplyStillRequestsRebuild()
    {
        var model = new AudioAnalysisSettingsViewModel(new());
        var changes = 0;
        var rebuilds = new List<AudioAnalysisPreferences>();
        model.Changed += (_, _) => changes++;
        model.RebuildRequested += (_, args) => rebuilds.Add(args.Preferences);
        model.FftSize = 2048;
        model.MinimumDecibelsText = "-100";
        Assert.True(model.Commit(AudioAnalysisSettingsField.MINIMUM_DECIBELS));
        model.SelectedWindow = model.WindowChoices.Single(choice => choice.Window == AudioSpectrumWindow.BLACKMAN);
        Assert.Equal(new AudioAnalysisRecipe(), model.Preferences.Recipe);
        Assert.Equal(0, changes);
        Assert.Empty(rebuilds);
        Assert.True(model.ApplyRecipe());
        Assert.True(model.ApplyRecipe());
        Assert.Equal(2, rebuilds.Count);
        Assert.Equal(2048, rebuilds[0].Recipe.FftSize);
        Assert.Equal(-100, rebuilds[0].Recipe.MinimumDecibels);
        Assert.Equal(AudioSpectrumWindow.BLACKMAN, rebuilds[0].Recipe.Window);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void CrossFieldRangesBlockApplyUntilCorrected()
    {
        var model = new AudioAnalysisSettingsViewModel(new());
        var rebuilds = 0;
        model.RebuildRequested += (_, _) => rebuilds++;
        model.SpectrumSampleRate = 8000;
        Assert.False(model.ApplyRecipe());
        Assert.Equal("8000", model.MaximumFrequencyText);
        model.MinimumFrequencyText = "3000";
        model.MaximumFrequencyText = "2000";
        Assert.False(model.ApplyRecipe());
        model.MaximumFrequencyText = "4000";
        model.MinimumDecibelsText = "-10";
        model.MaximumDecibelsText = "-5";
        Assert.False(model.ApplyRecipe());
        Assert.Equal(0, rebuilds);
        model.MinimumDecibelsText = "-80";
        Assert.True(model.ApplyRecipe());
        Assert.Equal(1, rebuilds);
        Assert.Null(model.Error);
    }

    [Fact]
    public void ResetAppliesOrdinaryDefaultsAndLeavesAdvancedDefaultsPending()
    {
        var recipe = new AudioAnalysisRecipe { FftSize = 4096, FrequencyBins = 512 };
        var model = new AudioAnalysisSettingsViewModel(new()
        {
            AudioAnalysis = new()
            {
                AdvancedMode = true, Recipe = recipe,
                Execution = new() { MemoryBudgetMiB = 256 }, Display = new() { WaveformGain = 4 }
            }
        });
        var changes = 0;
        var rebuilds = 0;
        model.Changed += (_, _) => changes++;
        model.RebuildRequested += (_, _) => rebuilds++;
        model.Reset();
        Assert.Equal(new AudioAnalysisExecutionOptions(), model.Preferences.Execution);
        Assert.Equal(new AudioAnalysisDisplayOptions(), model.Preferences.Display);
        Assert.Equal(recipe, model.Preferences.Recipe);
        Assert.Equal(1024, model.FftSize);
        Assert.Equal(1, changes);
        Assert.Equal(0, rebuilds);
        model.AdvancedMode = false;
        model.AdvancedMode = true;
        Assert.Equal(1024, model.FftSize);
        Assert.True(model.ApplyRecipe());
        Assert.Equal(new AudioAnalysisRecipe(), model.Preferences.Recipe);
        Assert.Equal(1, rebuilds);
    }

    [Fact]
    public void PreferenceAndLanguageRefreshKeepUnappliedRecipeAndRawDraft()
    {
        var model = new AudioAnalysisSettingsViewModel(new());
        model.FftSize = 2048;
        model.MinimumFrequencyText = "1e-";
        Assert.False(model.Commit(AudioAnalysisSettingsField.MINIMUM_FREQUENCY));
        var updates = 0;
        model.Changed += (_, _) => updates++;
        model.RebuildRequested += (_, _) => updates++;
        model.UpdatePreferences(new()
        {
            AudioAnalysis = new() { Recipe = new() { FftSize = 4096 }, Display = new() { SpectrumBrightness = 2 } }
        });
        model.RefreshLanguage();
        Assert.Equal(2048, model.FftSize);
        Assert.Equal("1e-", model.MinimumFrequencyText);
        Assert.Equal("2", model.SpectrumBrightnessText);
        Assert.NotNull(model.Error);
        Assert.Equal(0, updates);
    }
}
