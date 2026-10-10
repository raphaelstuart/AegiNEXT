using AegiNext.Application.ColorTags;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.ColorTags;
using AegiNext.Desktop.Startup;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleColorTagsSettingsUiTests
{
    [AvaloniaTheory]
    [InlineData(false, "en-US", 860, 580)]
    [InlineData(true, "en-US", 860, 580)]
    [InlineData(false, "zh-CN", 860, 580)]
    [InlineData(true, "zh-CN", 860, 580)]
    [InlineData(true, "en-US", 980, 720)]
    [InlineData(false, "zh-CN", 980, 720)]
    public async Task LibraryLayoutMatchesTheSettingsPagesAndShowsAllDefaultTags(bool dark, string language,
        double width, double height)
    {
        using var environment = new UiTestEnvironment();
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath));
        await application.Initialization;
        application.UpdatePreferences(value =>
            value with { Theme = dark ? WorkbenchTheme.DARK : WorkbenchTheme.LIGHT, Language = language });
        Localization.SetLanguage(language);
        using var coordinator = new SettingsWindowCoordinator(application, _ => new StartupTestDialogService());
        var owner = new Window();
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.SUBTITLE_COLOR_TAGS);
            var window = coordinator.Window!;
            window.Width = width;
            window.Height = height;
            Flush(window);
            var page = UiTestActions.Find<Grid>(window, "ColorTagsPage");
            var toolbar = UiTestActions.Find<StackPanel>(window, "ColorTagLibraryToolbar");
            var view = UiTestActions.Find<SubtitleColorTagsSettingsView>(window, "ColorTagsView");
            var list = UiTestActions.Find<ListBox>(window, "ColorTagList");
            var picker = UiTestActions.Find<ColorDraftInput>(window, "ColorTagColorInput");
            var viewportStart = page.TranslatePoint(new Point(), window)!.Value;
            foreach (var control in new Control[] { list, picker })
            {
                var start = control.TranslatePoint(new Point(), window)!.Value;
                Assert.InRange(start.X, viewportStart.X,
                    viewportStart.X + page.Bounds.Width - control.Bounds.Width);
                Assert.InRange(start.Y, viewportStart.Y,
                    viewportStart.Y + page.Bounds.Height - control.Bounds.Height);
            }

            var listStart = list.TranslatePoint(new(), page)!.Value;
            Assert.Equal(180, list.Bounds.Width);
            Assert.Equal(toolbar.Bounds.Height + 12, listStart.Y);
            Assert.Equal(page.Bounds.Height, listStart.Y + list.Bounds.Height);
            var name = UiTestActions.Find<TextBox>(window, "ColorTagNameInput");
            var nameStart = name.TranslatePoint(new(), page)!.Value;
            var pickerStart = picker.TranslatePoint(new(), page)!.Value;
            Assert.Equal(16, nameStart.X - listStart.X - list.Bounds.Width);
            Assert.Equal(nameStart.X, pickerStart.X);
            Assert.Equal(name.Bounds.Width, picker.Bounds.Width);
            var buttons = toolbar.Children.OfType<Button>().ToArray();
            Assert.Equal(4, buttons.Length);
            Assert.All(buttons, button => Assert.IsType<AegiNext.Desktop.Controls.Common.IconText>(button.Content));
            Assert.Contains("accent", UiTestActions.Find<Button>(window, "SaveColorTagsButton").Classes);

            Assert.True(picker.Bounds.Width >= 200);
            Assert.Equal(7, list.ItemCount);
            var listScroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(listScroll.Extent.Height <= listScroll.Viewport.Height,
                $"Default tags are clipped: content height={listScroll.Extent.Height}, viewport height={listScroll.Viewport.Height}.");
            Assert.Same(window.ViewModel.ColorTags, view.DataContext);
            Assert.Equal(Localization.Get("Settings.SubtitleColorTags"), window.ViewModel.PageTitle);
            Assert.True(view.IsEffectivelyVisible);
            var colors = UiTestActions.Find<UserControl>(window, "ColorsView");
            Assert.False(colors.IsEffectivelyVisible);
            Assert.DoesNotContain(colors.GetVisualDescendants(), control => control is SubtitleColorTagsSettingsView);
            UiTestActions.SelectSettingsPage(window, SettingsPage.COLORS);
            Flush(window);
            Assert.True(colors.IsEffectivelyVisible);
            Assert.False(view.IsEffectivelyVisible);
            UiTestActions.SelectSettingsPage(window, SettingsPage.SUBTITLE_COLOR_TAGS);
            Flush(window);
            Assert.True(view.IsEffectivelyVisible);
            Assert.False(colors.IsEffectivelyVisible);
            UiTestCapture.CaptureExportPanel(window,
                $"subtitle-color-tags-settings-{language}-{(dark ? "dark" : "light")}-{width}-{height}");
        }
        finally
        {
            if (coordinator.Window is { } settings)
            {
                settings.Close();
                await settings.CloseCompletion;
            }

            owner.Close();
        }
    }

    [AvaloniaFact]
    public async Task ActualNameAndRgbInputsSaveOnlyAfterTheLibraryWriteSucceedsAndCanRetry()
    {
        using var environment = new UiTestEnvironment();
        await using var application = new DesktopApplicationContext(new(environment.DirectoryPath));
        await application.Initialization;
        using var coordinator = new SettingsWindowCoordinator(application, _ => new StartupTestDialogService());
        var owner = new Window();
        try
        {
            owner.Show();
            await coordinator.OpenAsync(owner, page: SettingsPage.SUBTITLE_COLOR_TAGS);
            var window = coordinator.Window!;
            var model = window.ViewModel.ColorTags;
            var original = application.ColorTagLibrary.Snapshot;
            var selectedId = model.SelectedTag!.Id;
            var name = UiTestActions.Find<TextBox>(window, "ColorTagNameInput");
            name.BringIntoView();
            Flush(window);
            Assert.True(name.Focus());
            name.SelectAll();
            window.KeyTextInput("Reviewed 中文");
            var picker = UiTestActions.Find<ColorDraftInput>(window, "ColorTagColorInput");
            Assert.False(picker.Draft!.IsAlphaEnabled);
            var input = picker.FindControl<TextBox>("ColorInput")!;
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("#12AB34");
            UiTestActions.Press(window, Key.Enter);
            Assert.Same(original, application.ColorTagLibrary.Snapshot);
            Assert.True(model.IsDirty);
            var path = Path.Combine(environment.DirectoryPath, "subtitle-color-tags.json");
            File.Delete(path);
            Directory.CreateDirectory(path);

            UiTestActions.Click(window, "SaveColorTagsButton");
            await model.Completion;

            Assert.True(model.IsDirty);
            Assert.Same(original, application.ColorTagLibrary.Snapshot);
            Assert.Equal("Reviewed 中文", model.SelectedTag!.Name);
            Assert.Equal("#12AB34", model.SelectedTag.ColorDraft.HexText);
            Directory.Delete(path);
            UiTestActions.Click(window, "SaveColorTagsButton");
            await model.Completion;
            Assert.False(model.IsDirty);
            var saved = application.ColorTagLibrary.Snapshot.Tags.Single(tag => tag.Id == selectedId);
            Assert.Equal("Reviewed 中文", saved.Name);
            Assert.Equal("#12AB34", saved.ColorHex);
            Assert.True(File.Exists(path));
        }
        finally
        {
            if (coordinator.Window is { } settings)
            {
                settings.ViewModel.ColorTags.DiscardDraft();
                settings.Close();
                await settings.CloseCompletion;
            }

            owner.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task PageNavigationAndCloseHandleUnsavedTagLibraryDrafts(int choice)
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        var model = window.ViewModel.ColorTags;
        model.UpdateLibrary(new() { Tags = [new SubtitleColorTag { Name = "Review", ColorHex = "#DD4455" }] });
        var saves = 0;
        var decisions = 0;
        model.SaveDraftAsync = (value, _) =>
        {
            saves++;
            return Task.FromResult<SubtitleColorTagLibraryDocument?>(value);
        };
        model.ConfirmLeaveAsync = () =>
        {
            decisions++;
            return Task.FromResult(choice);
        };
        try
        {
            window.SelectPage(SettingsPage.SUBTITLE_COLOR_TAGS);
            window.Show();
            Flush(window);
            var input = UiTestActions.Find<TextBox>(window, "ColorTagNameInput");
            input.BringIntoView();
            Flush(window);
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("Changed");

            window.SelectPage(SettingsPage.APPEARANCE);
            await window.ViewModel.NavigationCompletion;

            Assert.Equal(1, decisions);
            Assert.Equal(choice == 2 ? SettingsPage.SUBTITLE_COLOR_TAGS : SettingsPage.APPEARANCE, window.CurrentPage);
            Assert.Equal(choice == 2, model.IsDirty);
            Assert.Equal(choice == 0 ? 1 : 0, saves);
            model.SelectedTag!.Name = "Another change";
            window.Close();
            await window.CloseCompletion;
            Assert.Equal(choice == 2, window.IsVisible);
            Assert.Equal(2, decisions);
        }
        finally
        {
            model.ConfirmLeaveAsync = () => Task.FromResult(1);
            window.Close();
            await window.CloseCompletion;
        }
    }

    [AvaloniaFact]
    public async Task InvalidRgbSurvivesThemeLanguageAndResetAndRestoreRemovesOnlyTagDrafts()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        var model = window.ViewModel.ColorTags;
        var original = new SubtitleColorTagLibraryDocument
        {
            Tags = [new SubtitleColorTag { Name = "Review", ColorHex = "#DD4455" }]
        };
        model.UpdateLibrary(original);
        model.SaveDraftAsync = (value, _) => Task.FromResult<SubtitleColorTagLibraryDocument?>(value);
        try
        {
            window.SelectPage(SettingsPage.SUBTITLE_COLOR_TAGS);
            window.Show();
            Flush(window);
            var child = UiTestActions.Find<SubtitleColorTagsSettingsView>(window, "ColorTagsView");
            Assert.Same(model, child.DataContext);
            var picker = UiTestActions.Find<ColorDraftInput>(window, "ColorTagColorInput");
            var input = picker.FindControl<TextBox>("ColorInput")!;
            input.BringIntoView();
            Flush(window);
            Assert.True(input.Focus());
            input.SelectAll();
            window.KeyTextInput("#bad");
            window.UpdatePreferences(new() { Theme = WorkbenchTheme.DARK });
            window.RefreshLanguage();
            window.ViewModel.Colors.ResetColorsCommand.Execute(null);
            Assert.Equal("#bad", input.Text);
            Assert.True(model.IsDirty);
            Assert.False(await model.SavePendingAsync());
            Assert.Equal(original.Tags[0].Name, model.SelectedTag!.Name);

            UiTestActions.Click(window, "RestoreColorTagsButton");
            Flush(window);
            Assert.False(model.IsDirty);
            Assert.Equal("#DD4455", UiTestActions.Find<ColorDraftInput>(window, "ColorTagColorInput").Draft!.HexText);
        }
        finally
        {
            model.DiscardDraft();
            window.Close();
            await window.CloseCompletion;
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
