using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class AudioCalibrationSettingsUiTests
{
    [AvaloniaTheory]
    [InlineData("invalid")]
    [InlineData("12.5")]
    [InlineData("1001")]
    [InlineData("-1001")]
    public void InvalidCalibrationStaysEditableAcrossBlurNavigationAndLanguageUntilEscape(string text)
    {
        using var environment = new UiTestEnvironment();
        var preferences = new WorkbenchPreferences
        {
            Language = "en-US", AudioCalibrations = [new("speaker", "test-system", 48000, 2, 25)]
        };
        var window = new SettingsWindow(preferences);
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.MEDIA);
            var model = window.ViewModel.Media;
            model.UpdateAudioStatus(Clock("speaker"));
            var requests = new List<AudioDeviceCalibration>();
            model.AudioCalibrationChanged += (_, request) => requests.Add(request.Calibration);
            var input = UiTestActions.Find<NumericDraftInput>(window, "AudioExtraDelayInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("25", input.RawText);
            Assert.True(box.Focus());
            box.Text = text;
            UiTestActions.Press(window, Key.Enter);
            Assert.NotNull(model.AudioCalibrationError);
            Assert.Equal(text, input.RawText);
            Assert.Empty(requests);
            Assert.True(Assert.IsType<ListBoxItem>(UiTestActions.Find<ListBox>(window, "Navigation").SelectedItem).Focus());
            window.SelectPage(SettingsPage.PREVIEW);
            Localization.SetLanguage("zh-CN");
            window.UpdatePreferences(preferences with { Theme = WorkbenchTheme.DARK });
            window.SelectPage(SettingsPage.MEDIA);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(text, input.RawText);
            Assert.Equal(text, box.Text);
            Assert.NotNull(model.AudioCalibrationError);
            Assert.True(box.Focus());
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("25", input.RawText);
            Assert.Null(model.AudioCalibrationError);
            Assert.Empty(requests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void DeviceAndOutputFormatReplacementRestoreOnlyTheMatchingProfile()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new()
        {
            AudioCalibrations = [new("speaker", "test-system", 48000, 2, 50), new("headphones", "test-system", 48000, 2, 125)]
        });
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.MEDIA);
            var model = window.ViewModel.Media;
            var requests = new List<AudioDeviceCalibration>();
            model.AudioCalibrationChanged += (_, request) => requests.Add(request.Calibration);
            model.UpdateAudioStatus(Clock("speaker"));
            var input = UiTestActions.Find<NumericDraftInput>(window, "AudioExtraDelayInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.Equal("50", input.RawText);
            Assert.True(box.Focus());
            box.Text = "old-device-draft";
            model.UpdateAudioStatus(Clock("headphones"));
            Assert.Equal("125", input.RawText);
            Assert.Equal("125", box.Text);
            box.Text = "160";
            UiTestActions.Press(window, Key.Enter);
            var request = Assert.Single(requests);
            Assert.Equal("headphones", request.DeviceId);
            Assert.Equal(160, request.ExtraDelayMilliseconds);
            model.UpdateAudioStatus(Clock("headphones") with { SampleRate = 44100 });
            Assert.Equal("0", input.RawText);
            Assert.Null(model.AudioCalibrationError);
            model.UpdateAudioStatus(Clock("headphones") with { Quality = AudioClockQuality.UNAVAILABLE });
            Assert.False(input.IsEffectivelyEnabled);
            Assert.False(model.CommitAudioCalibration());
            Assert.Single(requests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task MainWindowEnterSavesOneCalibrationAndBlurDoesNotSaveItAgainOrEditTheProject()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiCalibrationAudioOutput();
        await using var context = new MainWindowTestContext((_, _, target, _) =>
            Task.FromResult(new AudioPlaybackSession(source, output, target)));
        await context.Controller.OpenAsync("calibration-settings.mkv");
        var document = context.Session.DocumentSnapshot;
        var undoLabel = context.Session.Editor.UndoLabel;
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        try
        {
            settings.SelectPage(SettingsPage.MEDIA);
            var model = settings.ViewModel.Media;
            var requests = 0;
            model.AudioCalibrationChanged += (_, _) => requests++;
            var input = UiTestActions.Find<NumericDraftInput>(settings, "AudioExtraDelayInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(input.IsEffectivelyEnabled);
            Assert.True(box.Focus());
            box.Text = "85";
            Assert.Empty(context.Session.Preferences.AudioCalibrations);
            UiTestActions.Press(settings, Key.Enter);
            await context.Session.AudioCalibrationCompletion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            await context.Session.ApplicationContext.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, requests);
            Assert.Equal(85, Assert.Single(context.Session.Preferences.AudioCalibrations).ExtraDelayMilliseconds);
            Assert.Equal(context.Session.Preferences, context.Session.ApplicationContext.PreferencesStore.Load());
            Assert.True(Assert.IsType<ListBoxItem>(UiTestActions.Find<ListBox>(settings, "Navigation").SelectedItem).Focus());
            Dispatcher.UIThread.RunJobs();
            await context.Session.AudioCalibrationCompletion;
            Assert.Equal(1, requests);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.Equal(undoLabel, context.Session.Editor.UndoLabel);
            settings.Close();
            context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
            settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
            settings.SelectPage(SettingsPage.MEDIA);
            Assert.Equal("85", UiTestActions.Find<NumericDraftInput>(settings, "AudioExtraDelayInput").RawText);
            Assert.Equal(0, output.DisposeCount);
        }
        finally
        {
            settings.Close();
        }
    }

    private static AudioOutputClockSnapshot Clock(string deviceId)
    {
        return new(0, 0, 1, deviceId, "test-system", 0, AudioClockQuality.SYSTEM, 0);
    }
}
