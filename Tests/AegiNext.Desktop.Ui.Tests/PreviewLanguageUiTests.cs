using System.Globalization;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PreviewLanguageUiTests
{
    [AvaloniaFact]
    public async Task PlaybackLabelsUseSelectedLanguageImmediatelyAndIgnoreCallbackCulture()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var window = context.Window;
        window.GetCommand(WorkbenchCommand.OPEN_SETTINGS).Execute(null);
        var settings = Assert.Single(window.OwnedWindows.OfType<SettingsWindow>());
        UiTestActions.SelectLanguage(settings, "zh-CN");
        Assert.Equal("播放", PlaybackLabel(window));
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
        await context.Controller.PlayAsync();
        Assert.Equal("暂停", PlaybackLabel(window));
        await context.Controller.PauseAsync();
        Assert.Equal("播放", PlaybackLabel(window));
    }

    private static string? PlaybackLabel(Window window)
    {
        return AutomationProperties.GetName(UiTestActions.Find<Button>(window, "PlayButton"));
    }
}
