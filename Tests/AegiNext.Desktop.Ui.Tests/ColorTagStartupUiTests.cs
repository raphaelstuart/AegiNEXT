using AegiNext.Application.ColorTags;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Startup;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证真实 UI 启动时按已加载语言初始化个人颜色标签库。</summary>
public sealed class ColorTagStartupUiTests
{
    private static readonly string[] defaultColors =
        ["#FF3B30", "#FF9500", "#FFCC00", "#34C759", "#007AFF", "#AF52DE", "#8E8E93"];

    /// <summary>首次建库按磁盘语言注入七色，主动清空后重新启动保持空库。</summary>
    [AvaloniaFact]
    public async Task MissingLibraryInitializesSevenColorsInTheLoadedLanguageAndEmptyLibraryStaysEmpty()
    {
        using var environment = new UiTestEnvironment();
        using (var preferences = new WorkbenchPreferencesStore(environment.DirectoryPath))
        {
            await preferences.SaveAsync(new() { Language = "zh-CN" }, TestContext.Current.CancellationToken);
        }
        Assert.Equal("en-US", Localization.CurrentLanguageID);
        var path = Path.Combine(environment.DirectoryPath, "subtitle-color-tags.json");
        await using (var context = new DesktopApplicationContext(new(environment.DirectoryPath)))
        {
            var library = context.ColorTagLibrary;
            await context.Initialization;

            Assert.Equal("zh-CN", context.Preferences.Language);
            Assert.Equal("zh-CN", Localization.SelectedLanguageID);
            Assert.Equal("zh-CN", Localization.CurrentLanguageID);
            Assert.Same(library, context.ColorTagLibrary);
            Assert.Equal(defaultColors, context.ColorTagLibrary.Snapshot.Tags.Select(tag => tag.ColorHex));
            Assert.Equal("红色", context.ColorTagLibrary.Snapshot.Tags[0].Name);
            Assert.Equal(Localization.Get("Settings.ColorTagDefault.Red"), context.ColorTagLibrary.Snapshot.Tags[0].Name);
            Assert.True(File.Exists(path));
            await context.RunColorTagOperationAsync(() => context.ColorTagLibrary.ReplaceAsync(new()));
        }
        await using var reopened = new DesktopApplicationContext(new(environment.DirectoryPath));
        await reopened.Initialization;

        Assert.Empty(reopened.ColorTagLibrary.Snapshot.Tags);
        Assert.Empty((await SubtitleColorTagStore.LoadAsync(path, TestContext.Current.CancellationToken)).Tags);
    }
}
