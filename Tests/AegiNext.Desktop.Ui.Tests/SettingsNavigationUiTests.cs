using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证分组导航的实际输入、页面映射和本地化布局。</summary>
public sealed class SettingsNavigationUiTests
{
    /// <summary>上下键跳过分类标题，并保持导航选中项与页面一致。</summary>
    [AvaloniaFact]
    public void KeyboardNavigationSkipsSectionHeadingsAndOpensTheMatchingPage()
    {
        using var environment = new UiTestEnvironment();
        var window = new SettingsWindow(new());
        try
        {
            window.Show();
            var navigation = UiTestActions.Find<ListBox>(window, "Navigation");
            var items = navigation.Items.Cast<ListBoxItem>().ToArray();
            var headings = items.Where(item => item.DataContext is not SettingsPage).ToArray();
            Assert.Equal(4, headings.Length);
            Assert.All(headings, heading =>
            {
                Assert.False(heading.IsEnabled);
                Assert.False(heading.Focusable);
            });
            var pages = items.Where(item => item.DataContext is SettingsPage).ToArray();
            Assert.Equal(Enum.GetValues<SettingsPage>().Length, pages.Length);
            Assert.Equal(pages.Length, pages.Select(item => item.DataContext).Distinct().Count());
            Assert.True(pages[0].Focus());

            foreach (var item in pages)
            {
                Assert.Same(item, navigation.SelectedItem);
                Assert.Equal(item.DataContext, window.CurrentPage);
                Assert.Equal(item.DataContext, navigation.SelectedValue);
                Assert.Equal(window.ViewModel.PageTitle, UiTestActions.Find<TextBlock>(window, "PageTitle").Text);
                UiTestActions.Press(window, Key.Down);
            }

            for (var index = pages.Length - 1; index >= 0; index--)
            {
                Assert.Same(pages[index], navigation.SelectedItem);
                Assert.Equal(pages[index].DataContext, window.CurrentPage);
                UiTestActions.Press(window, Key.Up);
            }
        }
        finally
        {
            window.Close();
        }
    }

    /// <summary>分类标题实时切换语言，较小窗口仍能滚动访问所有设置。</summary>
    [AvaloniaTheory]
    [InlineData("en-US", false, 980, 720)]
    [InlineData("en-US", true, 860, 580)]
    [InlineData("zh-CN", false, 860, 580)]
    [InlineData("zh-CN", true, 980, 720)]
    public void SectionHeadingsRefreshLanguageAndEveryPageRemainsReachable(string language, bool dark,
        int width, int height)
    {
        using var environment = new UiTestEnvironment();
        Localization.SetLanguage(language);
        var window = new SettingsWindow(new() { Language = language })
        {
            Width = width,
            Height = height,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        try
        {
            window.Show();
            var navigation = UiTestActions.Find<ListBox>(window, "Navigation");
            var headings = navigation.GetVisualDescendants().OfType<TextBlock>()
                .Where(text => text.Classes.Contains("navigation-section-title")).ToArray();
            var keys = new[]
            {
                "Settings.NavigationGeneral", "Settings.NavigationSubtitles",
                "Settings.NavigationMedia", "Settings.NavigationProjectData"
            };
            Assert.Equal(keys.Select(Localization.Get), headings.Select(text => text.Text));
            Assert.All(headings, heading =>
            {
                Assert.True(heading.Bounds.Height >= heading.LineHeight);
                Assert.True(heading.Bounds.Width >= heading.DesiredSize.Width);
            });
            foreach (var page in Enum.GetValues<SettingsPage>())
            {
                window.SelectPage(page);
                Assert.Equal(page, navigation.SelectedValue);
            }
            window.SelectPage(SettingsPage.APPEARANCE);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var scroll = navigation.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.True(scroll.Extent.Width <= scroll.Viewport.Width);
            if (height == 580)
            {
                Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
                window.SelectPage(SettingsPage.TRANSFER);
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var selected = Assert.IsType<ListBoxItem>(navigation.SelectedItem);
                var point = selected.TranslatePoint(new(0, 0), scroll)!.Value;
                Assert.InRange(point.Y, 0, scroll.Viewport.Height - selected.Bounds.Height);
            }

            var artifactDirectory = Environment.GetEnvironmentVariable("AEGINEXT_SETTINGS_NAVIGATION_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(artifactDirectory))
            {
                Directory.CreateDirectory(artifactDirectory);
                window.SelectPage(SettingsPage.APPEARANCE);
                scroll.ScrollToHome();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(artifactDirectory, $"settings-{language}-{(dark ? "dark" : "light")}-{width}.png"), PngBitmapEncoderOptions.Default);
            }

            var currentPage = window.CurrentPage;
            Localization.SetLanguage(language == "en-US" ? "zh-CN" : "en-US");
            Assert.Equal(keys.Select(Localization.Get), headings.Select(text => text.Text));
            Assert.Equal(currentPage, window.CurrentPage);
            Assert.Equal(currentPage, navigation.SelectedValue);
        }
        finally
        {
            window.Close();
        }
    }
}
