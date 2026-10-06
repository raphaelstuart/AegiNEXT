using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewSettingsUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewPageCommitsOnEnterOrBlurAndPersistsThroughTheSharedCoordinator(bool blur)
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new(environment.DirectoryPath), new()
        {
            Language = "en-US", TimelineClassicTimingEnabled = true, Volume = 0.375f
        });
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.PREVIEW);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            var changes = 0;
            window.PreviewChanged += (_, _) => changes++;
            Assert.True(window.ViewModel.IsPreviewVisible);
            Assert.False(window.ViewModel.IsMediaVisible);
            var input = UiTestActions.Find<NumericDraftInput>(window, "SubtitleAuditionMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            box.Text = "750";
            Assert.Equal(500, context.Preferences.SubtitleAuditionMilliseconds);
            if (blur)
            {
                Assert.True(Assert.IsType<ListBoxItem>(UiTestActions.Find<ListBox>(window, "Navigation").SelectedItem).Focus());
            }
            else
            {
                UiTestActions.Press(window, Key.Enter);
            }

            Dispatcher.UIThread.RunJobs();
            await context.Completion;
            Assert.Equal(1, changes);
            Assert.Equal(750, context.Preferences.SubtitleAuditionMilliseconds);
            Assert.True(context.Preferences.TimelineClassicTimingEnabled);
            Assert.Equal(0.375f, context.Preferences.Volume);
            Assert.Equal("en-US", context.Preferences.Language);
            Assert.Equal(context.Preferences, context.PreferencesStore.Load());
            coordinator.Close();
            await coordinator.OpenAsync(owner, page: SettingsPage.PREVIEW);
            var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.Equal("750", reopened.ViewModel.Preview.SubtitleAuditionMillisecondsText);
            Assert.False(reopened.ViewModel.HasError);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaTheory]
    [InlineData("750.")]
    [InlineData("750.5")]
    [InlineData("0")]
    [InlineData("2147483648")]
    public void InvalidMillisecondDraftSurvivesBlurNavigationAndLanguageUntilEscape(string text)
    {
        using var environment = new UiTestEnvironment();
        var preferences = new WorkbenchPreferences { Language = "en-US" };
        var window = new SettingsWindow(preferences);
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.PREVIEW);
            var changes = 0;
            window.PreviewChanged += (_, _) => changes++;
            var input = UiTestActions.Find<NumericDraftInput>(window, "SubtitleAuditionMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            box.Text = text;
            UiTestActions.Press(window, Key.Enter);
            Assert.True(window.ViewModel.HasError);
            Assert.True(Assert.IsType<ListBoxItem>(UiTestActions.Find<ListBox>(window, "Navigation").SelectedItem).Focus());
            window.SelectPage(SettingsPage.MEDIA);
            Localization.SetLanguage("zh-CN");
            window.UpdatePreferences(preferences with { Theme = WorkbenchTheme.DARK });
            window.SelectPage(SettingsPage.PREVIEW);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(text, input.RawText);
            Assert.Equal(text, box.Text);
            Assert.Equal("预览", window.ViewModel.PageTitle);
            Assert.True(window.ViewModel.HasError);
            Assert.Equal(500, window.ViewModel.Preview.SubtitleAuditionMilliseconds);
            Assert.Equal(0, changes);
            Assert.True(box.Focus());
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal("500", input.RawText);
            Assert.False(window.ViewModel.HasError);
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ImeConfirmationKeepsMillisecondDraftUntilCompositionEnds()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            window.SelectPage(SettingsPage.PREVIEW);
            var changes = 0;
            window.PreviewChanged += (_, _) => changes++;
            var input = UiTestActions.Find<NumericDraftInput>(window, "SubtitleAuditionMillisecondsInput");
            var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(box.Focus());
            box.Text = "750";
            var presenter = Assert.Single(box.GetVisualDescendants().OfType<TextPresenter>());
            presenter.PreeditText = "毫秒候选";

            UiTestActions.Press(window, Key.Enter);
            UiTestActions.Press(window, Key.Escape);

            Assert.Equal(0, changes);
            Assert.Equal("750", input.RawText);
            Assert.Equal(500, window.ViewModel.Preview.SubtitleAuditionMilliseconds);
            presenter.PreeditText = null;
            UiTestActions.Press(window, Key.Enter);
            Assert.Equal(1, changes);
            Assert.Equal(750, window.ViewModel.Preview.SubtitleAuditionMilliseconds);
        }
        finally
        {
            window.Close();
        }
    }
}
