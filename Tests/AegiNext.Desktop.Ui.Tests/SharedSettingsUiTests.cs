using AegiNext.Core.Presets;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using AegiNext.Media.Decoding;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SharedSettingsUiTests
{
    [AvaloniaFact]
    public async Task SettingsWithoutAProjectOfferAllPagesAndPersistPersonalTemplatesAndDecodeDefaults()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                window.SelectPage(page);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.Equal(page, window.CurrentPage);
                Assert.False(window.ViewModel.HasError);
            }

            window.SelectPage(SettingsPage.STYLES);
            UiTestActions.Click(window, "AddStyleButton");
            Assert.False(UiTestActions.Find<Button>(window, "CaptureStyleButton").IsEffectivelyEnabled);
            Assert.False(window.ViewModel.Styles.HasPositionMeasurementError);
            UiTestActions.Find<TextBox>(window, "StyleNameInput").Text = "个人样式 01";
            UiTestActions.Click(window, "SaveStyleButton");
            await context.Completion;
            Assert.Equal("个人样式 01", Assert.Single(context.StyleLibrary.Snapshot.Presets).Name);

            window.SelectPage(SettingsPage.EFFECTS);
            UiTestActions.Click(window, "AddEffectScriptButton");
            UiTestActions.Find<TextBox>(window, "EffectScriptNameInput").Text = "个人特效 01";
            UiTestActions.Click(window, "SaveEffectScriptButton");
            await context.Completion;
            Assert.Equal("个人特效 01", Assert.Single(context.EffectScriptLibrary.Snapshot.Presets).Name);

            window.SelectPage(SettingsPage.MEDIA);
            Assert.Equal(AegiNext.Desktop.I18n.Localization.Get("Settings.DecodeNoMedia"), window.ViewModel.Media.DecodeStatus);
            UiTestActions.Find<ComboBox>(window, "PreviewDecodeModeCombo").SelectedItem =
                window.ViewModel.Media.DecodeModes.Single(value => value.Mode == VideoDecodeMode.Software);
            await context.Completion;
            Assert.Equal(VideoDecodeMode.Software, context.Preferences.PreviewDecodeMode);
            Assert.Equal(VideoDecodeMode.Software, context.PreferencesStore.Load().PreviewDecodeMode);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task BusyAndAppearanceNotificationsPreserveUnsavedStyleAndEffectDrafts()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        var styleRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var styleStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var effectStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task? styleOperation = null;
        Task? effectOperation = null;
        try
        {
            await context.Initialization;
            var saved = new SubtitleStylePreset(Guid.NewGuid(), "Saved", new());
            await context.RunStyleOperationAsync(() => context.StyleLibrary.UpsertAsync(saved));
            owner.Show();
            await coordinator.OpenAsync(owner);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            window.SelectPage(SettingsPage.STYLES);
            UiTestActions.Find<TextBox>(window, "StyleNameInput").Text = "Unsaved 样式";
            window.ViewModel.Styles.FontSizeText = "invalid draft";
            var draftVersion = window.ViewModel.Styles.DraftVersion;
            window.ViewModel.Effects.AddCommand.Execute(null);
            window.ViewModel.Effects.Source = "effect broken\n";
            window.ViewModel.Effects.ValidateCommand.Execute(null);
            var effectId = window.ViewModel.Effects.Draft!.Id;
            var diagnosticLine = window.ViewModel.Effects.DiagnosticLine;

            styleOperation = context.RunStyleOperationAsync(() =>
            {
                styleStarted.TrySetResult();
                return styleRelease.Task;
            });
            effectOperation = context.RunEffectOperationAsync(() =>
            {
                effectStarted.TrySetResult();
                return effectRelease.Task;
            });
            await Task.WhenAll(styleStarted.Task, effectStarted.Task).WaitAsync(TimeSpan.FromSeconds(5));
            Dispatcher.UIThread.RunJobs();
            Assert.True(window.ViewModel.Styles.IsBusy);
            Assert.True(window.ViewModel.Effects.IsBusy);
            context.UpdatePreferences(value => value with { Theme = WorkbenchTheme.DARK });
            styleRelease.SetResult();
            effectRelease.SetResult();
            await Task.WhenAll(styleOperation, effectOperation);

            Assert.Equal(draftVersion, window.ViewModel.Styles.DraftVersion);
            Assert.Equal("Unsaved 样式", window.ViewModel.Styles.Name);
            Assert.Equal("invalid draft", window.ViewModel.Styles.FontSizeText);
            Assert.Equal(effectId, window.ViewModel.Effects.Draft!.Id);
            Assert.Equal("effect broken\n", window.ViewModel.Effects.Source);
            Assert.Equal(diagnosticLine, window.ViewModel.Effects.DiagnosticLine);
            Assert.False(window.ViewModel.Styles.IsBusy);
            Assert.False(window.ViewModel.Effects.IsBusy);
        }
        finally
        {
            styleRelease.TrySetResult();
            effectRelease.TrySetResult();
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task ClosingSettingsReleasesAllBindingsAndReopeningReadsTheLatestPreferences()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner);
            var closed = Assert.IsType<SettingsWindow>(coordinator.Window);
            coordinator.Close();
            Assert.Null(coordinator.Window);
            Assert.False(closed.IsVisible);

            closed.ViewModel.Appearance.ThemeIndex = (int)WorkbenchTheme.DARK;
            Assert.Equal(WorkbenchTheme.SYSTEM, context.Preferences.Theme);
            context.UpdatePreferences(value => value with { Theme = WorkbenchTheme.LIGHT });
            Assert.Equal((int)WorkbenchTheme.DARK, closed.ViewModel.Appearance.ThemeIndex);
            await coordinator.OpenAsync(owner, page: SettingsPage.COLORS);
            var reopened = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.NotSame(closed, reopened);
            Assert.Equal(SettingsPage.COLORS, reopened.CurrentPage);
            Assert.Equal((int)WorkbenchTheme.LIGHT, reopened.ViewModel.Appearance.ThemeIndex);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task SharedLibraryFailureIsVisibleOnTheEffectPageAndDoesNotBreakFollowingOperations()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.EFFECTS);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            await Assert.ThrowsAsync<IOException>(() => context.RunEffectOperationAsync(() =>
                Task.FromException(new IOException("Storage unavailable"))));
            Assert.Equal("Storage unavailable", window.ViewModel.Error);
            Assert.True(window.ViewModel.HasError);
            Assert.False(window.ViewModel.Effects.IsBusy);

            UiTestActions.Click(window, "AddEffectScriptButton");
            UiTestActions.Click(window, "SaveEffectScriptButton");
            await context.Completion;
            Assert.Single(context.EffectScriptLibrary.Snapshot.Presets);
            Assert.False(window.ViewModel.HasError);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }

    [AvaloniaFact]
    public async Task WelcomeSettingsUseAModalLifetimeAndReuseTheExistingWindow()
    {
        using var environment = new UiTestEnvironment();
        var context = new DesktopApplicationContext(new WorkbenchPreferencesStore(environment.DirectoryPath));
        using var coordinator = new SettingsWindowCoordinator(context);
        var owner = new Window();
        try
        {
            await context.Initialization;
            owner.Show();
            var modal = coordinator.OpenAsync(owner, page: SettingsPage.MEDIA, modal: true);
            var window = Assert.IsType<SettingsWindow>(coordinator.Window);
            Assert.False(modal.IsCompleted);
            await coordinator.OpenAsync(owner, page: SettingsPage.STYLES);
            Assert.Same(window, coordinator.Window);
            Assert.Equal(SettingsPage.STYLES, window.CurrentPage);
            Assert.Single(owner.OwnedWindows.OfType<SettingsWindow>());
            coordinator.Close();
            await modal;
            Assert.Null(coordinator.Window);
        }
        finally
        {
            coordinator.Close();
            owner.Close();
            await context.DisposeAsync();
        }
    }
}
