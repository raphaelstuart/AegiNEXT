using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Tests;

/// <summary>Verifies explicit language selection for workbench and preview resources.</summary>
public sealed class WorkbenchLocalizationTests
{
    /// <summary>One selected language resolves both workbench and preview text.</summary>
    [Theory]
    [InlineData("zh-CN", "保存", "播放")]
    [InlineData("en-US", "Save", "Play")]
    public void ExplicitLanguageResolvesWorkbenchAndPlaybackText(string languageID, string saveText, string playText)
    {
        var catalog = LocalizationCatalog.Load(Path.Combine(AppContext.BaseDirectory, "i18n"));

        Assert.Equal(saveText, catalog.Get(languageID, "Workbench.Save"));
        Assert.Equal(playText, catalog.Get(languageID, "Preview.Play"));
    }
}
