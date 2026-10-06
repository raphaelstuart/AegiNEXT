using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Media.Decoding;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MediaSettingsUiTests
{
    [AvaloniaFact]
    public async Task MainWindowWiringSavesDecoderChoiceWithoutEditingTheProjectAndReopensIt()
    {
        await using var context = new MainWindowTestContext();
        var document = context.Session.DocumentSnapshot;
        context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        try
        {
            settings.SelectPage(SettingsPage.MEDIA);
            var selector = UiTestActions.Find<ComboBox>(settings, "PreviewDecodeModeCombo");
            selector.SelectedIndex = 1;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(VideoDecodeMode.Software, context.Session.Preferences.PreviewDecodeMode);
            Assert.Equal(VideoDecodeMode.Software, context.Controller.DecodeMode);
            Assert.Same(document, context.Session.DocumentSnapshot);
            var directory = Environment.GetEnvironmentVariable("AEGINEXT_PREFERENCES_DIRECTORY")!;
            using var saved = new WorkbenchPreferencesStore(directory);
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (saved.Load().PreviewDecodeMode != VideoDecodeMode.Software)
            {
                Assert.True(DateTime.UtcNow < deadline, "Decoder preference was not persisted.");
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }

            settings.Close();
            context.Window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
            settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
            settings.SelectPage(SettingsPage.MEDIA);
            selector = UiTestActions.Find<ComboBox>(settings, "PreviewDecodeModeCombo");
            Assert.Equal(VideoDecodeMode.Software, Assert.IsType<VideoDecodeModeChoice>(selector.SelectedItem).Mode);
        }
        finally
        {
            settings.Close();
        }
    }

    [AvaloniaFact]
    public void RealDecoderSelectorEmitsAllModesAndPreservesSelectionAcrossLanguageAndRollback()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.MEDIA);
            var selector = UiTestActions.Find<ComboBox>(window, "PreviewDecodeModeCombo");
            var requests = new List<VideoDecodeMode>();
            window.PreviewDecodeModeChanged += (_, value) => requests.Add(value.Mode);
            Assert.Same(window.ViewModel.Media, UiTestActions.Find<MediaSettingsView>(window, "MediaView").DataContext);
            Assert.Equal(3, selector.ItemCount);
            Assert.Equal(VideoDecodeMode.Auto, Assert.IsType<VideoDecodeModeChoice>(selector.SelectedItem).Mode);

            selector.SelectedIndex = 1;
            selector.SelectedIndex = 2;
            selector.SelectedIndex = 0;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(new[] { VideoDecodeMode.Software, VideoDecodeMode.Hardware, VideoDecodeMode.Auto }, requests);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(VideoDecodeMode.Auto, Assert.IsType<VideoDecodeModeChoice>(selector.SelectedItem).Mode);
            Assert.Equal("自动", Assert.IsType<VideoDecodeModeChoice>(selector.SelectedItem).Label);
            Assert.Equal("媒体", UiTestActions.Find<TextBlock>(window, "PageTitle").Text);
            Assert.Equal(3, requests.Count);
            selector.SelectedIndex = 2;
            window.ViewModel.Media.IsBusy = true;
            Assert.False(selector.IsEffectivelyEnabled);
            window.UpdatePreferences(new() { PreviewDecodeMode = VideoDecodeMode.Software });
            Assert.Equal(VideoDecodeMode.Hardware, Assert.IsType<VideoDecodeModeChoice>(selector.SelectedItem).Mode);
            window.ViewModel.Media.IsBusy = false;
            window.UpdatePreferences(new() { PreviewDecodeMode = VideoDecodeMode.Software });
            Assert.Equal(VideoDecodeMode.Software, Assert.IsType<VideoDecodeModeChoice>(selector.SelectedItem).Mode);
            Assert.Equal(4, requests.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActualBackendAndFallbackAreVisibleAndTranslateWithoutChangingTheRequestedMode()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.MEDIA);
            window.ViewModel.Media.UpdateDecodeStatus("h264", false, "device unavailable");
            var status = UiTestActions.Find<TextBlock>(window, "DecodeStatus");
            Assert.Contains("CPU", status.Text);
            Assert.Contains("device unavailable", status.Text);
            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();
            Assert.Contains("GPU 回退原因", status.Text);
            Assert.Equal(VideoDecodeMode.Auto, window.ViewModel.Media.SelectedDecodeMode!.Mode);
        }
        finally
        {
            window.Close();
        }
    }
}
