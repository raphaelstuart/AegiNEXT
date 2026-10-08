using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.AudioAnalysis;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AudioAnalysisSettingsUiTests
{
    [AvaloniaFact]
    public void OrdinaryKeyboardCommitAndEscapePreserveOtherDraftsAcrossNavigationAndLanguage()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.AUDIO_ANALYSIS);
            Assert.Equal("Audio analysis", window.ViewModel.PageTitle);
            Assert.False(UiTestActions.Find<ComboBox>(window, "SegmentSamplesCombo").IsEffectivelyVisible);
            var changes = new List<AudioAnalysisPreferences>();
            var rebuilds = 0;
            window.AudioAnalysisChanged += (_, args) => changes.Add(args.Preferences);
            window.AudioAnalysisRebuildRequested += (_, _) => rebuilds++;
            var input = UiTestActions.Find<NumericDraftInput>(window, "WaveformGainInput");
            Edit(window, input, "2");
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(2, Assert.Single(changes).Display.WaveformGain);
            Edit(window, input, "1e-");
            UiTestActions.Press(window, Key.Enter);
            Assert.True(window.ViewModel.HasError);
            window.SelectPage(SettingsPage.MEDIA);
            Localization.SetLanguage("zh-CN");
            window.UpdatePreferences(new() { AudioAnalysis = new() { Display = new() { WaveformGain = 3 } } });
            window.SelectPage(SettingsPage.AUDIO_ANALYSIS);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("音频分析", window.ViewModel.PageTitle);
            Assert.Equal("1e-", input.RawText);
            window.ViewModel.AudioAnalysis.MemoryBudgetText = "192";
            Assert.True(input.FocusInput());
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("3", input.RawText);
            Assert.Equal("192", window.ViewModel.AudioAnalysis.MemoryBudgetText);
            Assert.False(window.ViewModel.HasError);
            Assert.Single(changes);
            Assert.Equal(0, rebuilds);
        }
        finally
        {
            window.Close();
            Assert.False(window.IsVisible);
        }
    }

    [AvaloniaFact]
    public void AdvancedDraftAppliesOnlyByButtonAndResetWaitsForAnotherApply()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { AudioAnalysis = new() { AdvancedMode = true } });
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.AUDIO_ANALYSIS);
            var changes = new List<AudioAnalysisPreferences>();
            var rebuilds = new List<AudioAnalysisPreferences>();
            window.AudioAnalysisChanged += (_, args) => changes.Add(args.Preferences);
            window.AudioAnalysisRebuildRequested += (_, args) =>
            {
                rebuilds.Add(args.Preferences);
                window.UpdatePreferences(new() { AudioAnalysis = args.Preferences });
            };
            var model = window.ViewModel.AudioAnalysis;
            Assert.True(UiTestActions.Find<ComboBox>(window, "SegmentSamplesCombo").IsEffectivelyVisible);
            UiTestActions.Find<ComboBox>(window, "FftSizeCombo").SelectedItem = 2048;
            var input = UiTestActions.Find<NumericDraftInput>(window, "MinimumDecibelsInput");
            Edit(window, input, "-100");
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(1024, model.Preferences.Recipe.FftSize);
            Assert.Empty(changes);
            Assert.Empty(rebuilds);
            UiTestActions.Click(window, "ApplyAudioRecipeButton");
            Assert.Equal(2048, Assert.Single(rebuilds).Recipe.FftSize);
            Assert.Equal(-100, rebuilds[0].Recipe.MinimumDecibels);
            UiTestActions.Click(window, "ApplyAudioRecipeButton");
            Assert.Equal(2, rebuilds.Count);
            model.WaveformGainText = "2";
            Assert.True(model.Commit(AudioAnalysisSettingsField.WAVEFORM_GAIN));
            UiTestActions.Click(window, "ResetAudioAnalysisButton");
            Assert.Equal(1, model.Preferences.Display.WaveformGain);
            Assert.Equal(2048, model.Preferences.Recipe.FftSize);
            Assert.Equal(1024, model.FftSize);
            Assert.Equal(2, rebuilds.Count);
            UiTestActions.Click(window, "ApplyAudioRecipeButton");
            Assert.Equal(1024, rebuilds[2].Recipe.FftSize);
            Assert.Equal(-80, rebuilds[2].Recipe.MinimumDecibels);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void LowerSampleRateNeedsValidFrequencyAndCollapsePreservesInvalidRawText()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new() { AudioAnalysis = new() { AdvancedMode = true } });
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.AUDIO_ANALYSIS);
            var rebuilds = new List<AudioAnalysisPreferences>();
            window.AudioAnalysisRebuildRequested += (_, args) => rebuilds.Add(args.Preferences);
            UiTestActions.Find<ComboBox>(window, "SpectrumSampleRateCombo").SelectedItem = 8000;
            UiTestActions.Click(window, "ApplyAudioRecipeButton");
            Assert.Empty(rebuilds);
            Assert.True(window.ViewModel.HasError);
            var input = UiTestActions.Find<NumericDraftInput>(window, "MaximumFrequencyInput");
            Assert.Equal("8000", input.RawText);
            Edit(window, input, "4e-");
            UiTestActions.Press(window, Key.Enter);
            var advanced = UiTestActions.Find<CheckBox>(window, "AudioAdvancedCheckBox");
            advanced.IsChecked = false;
            advanced.IsChecked = true;
            Assert.Equal("4e-", input.RawText);
            Assert.Equal(8000, window.ViewModel.AudioAnalysis.SpectrumSampleRate);
            Edit(window, input, "4000");
            UiTestActions.Click(window, "ApplyAudioRecipeButton");
            Assert.Equal(4000, Assert.Single(rebuilds).Recipe.MaximumFrequency);
            Assert.False(window.ViewModel.HasError);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ImportedWorkerRequestIsNotOverwrittenByControlRangeProjection()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new()
        {
            AudioAnalysis = new() { Execution = new() { MaximumWorkers = 1024 } }
        });
        try
        {
            var changes = 0;
            window.AudioAnalysisChanged += (_, _) => changes++;
            window.Show();
            window.SelectPage(SettingsPage.AUDIO_ANALYSIS);
            var input = UiTestActions.Find<NumericDraftInput>(window, "MaximumWorkersInput");
            Assert.Equal("1024", input.RawText);
            Assert.True(input.FocusInput());
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(1024, window.ViewModel.AudioAnalysis.Preferences.Execution.MaximumWorkers);
            Assert.Equal("1024", input.RawText);
            Assert.False(window.ViewModel.HasError);
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Edit(Window window, NumericDraftInput input, string text)
    {
        Assert.True(input.FocusInput());
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        box.SelectAll();
        window.KeyTextInput(text);
    }
}
