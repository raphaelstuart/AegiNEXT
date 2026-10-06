using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Preview;

namespace AegiNext.Desktop.Tests;

public sealed class PreviewSettingsViewModelTests
{
    [Theory]
    [InlineData("1", 1)]
    [InlineData("750", 750)]
    [InlineData("60001", 60001)]
    [InlineData("2147483647", int.MaxValue)]
    public void PositiveIntegerDraftCommitsOnceAndReloadDoesNotEmit(string text, int milliseconds)
    {
        var model = new PreviewSettingsViewModel(new());
        var changes = new List<int>();
        model.Changed += (_, change) => changes.Add(change.SubtitleAuditionMilliseconds);
        model.SubtitleAuditionMillisecondsText = text;

        Assert.Empty(changes);
        Assert.True(model.CommitSubtitleAuditionMilliseconds());
        Assert.True(model.CommitSubtitleAuditionMilliseconds());
        model.UpdatePreferences(new() { SubtitleAuditionMilliseconds = milliseconds, Theme = WorkbenchTheme.DARK });
        model.RefreshLanguage();

        Assert.Equal(milliseconds, Assert.Single(changes));
        Assert.Equal(milliseconds, model.SubtitleAuditionMilliseconds);
        Assert.Equal(text, model.SubtitleAuditionMillisecondsText);
        Assert.Null(model.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("750.")]
    [InlineData("750.5")]
    [InlineData("1e3")]
    [InlineData("2147483648")]
    [InlineData("NaN")]
    public void InvalidDraftRemainsEditableAndDoesNotChangeTheConfirmedValue(string text)
    {
        var model = new PreviewSettingsViewModel(new());
        var changes = 0;
        model.Changed += (_, _) => changes++;
        model.SubtitleAuditionMillisecondsText = text;

        Assert.False(model.CommitSubtitleAuditionMilliseconds());
        model.UpdatePreferences(new() { Theme = WorkbenchTheme.DARK, TimelineClassicTimingEnabled = true });
        model.RefreshLanguage();

        Assert.Equal(text, model.SubtitleAuditionMillisecondsText);
        Assert.Equal(500, model.SubtitleAuditionMilliseconds);
        Assert.NotNull(model.Error);
        Assert.Equal(0, changes);
        model.RestoreSubtitleAuditionMilliseconds();
        Assert.Equal("500", model.SubtitleAuditionMillisecondsText);
        Assert.Null(model.Error);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void SharedPreferenceChangesPreserveTheDraftAndEscapeUsesTheLatestConfirmedValue()
    {
        var model = new PreviewSettingsViewModel(new());
        var changes = 0;
        model.Changed += (_, _) => changes++;
        model.SubtitleAuditionMillisecondsText = "8e-";
        Assert.False(model.CommitSubtitleAuditionMilliseconds());

        model.UpdatePreferences(new() { SubtitleAuditionMilliseconds = 750, TimelineClassicTimingEnabled = true });

        Assert.Equal("8e-", model.SubtitleAuditionMillisecondsText);
        Assert.Equal(750, model.SubtitleAuditionMilliseconds);
        Assert.NotNull(model.Error);
        model.RestoreSubtitleAuditionMilliseconds();
        Assert.Equal("750", model.SubtitleAuditionMillisecondsText);
        Assert.Null(model.Error);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void PreviewPageIsIndependentFromMediaAndNavigationPreservesItsInvalidDraft()
    {
        var model = new SettingsWindowViewModel(new());
        model.PageIndex = (int)SettingsPage.PREVIEW;
        model.Preview.SubtitleAuditionMillisecondsText = "0";
        Assert.False(model.Preview.CommitSubtitleAuditionMilliseconds());

        model.PageIndex = (int)SettingsPage.MEDIA;
        Assert.True(model.IsMediaVisible);
        Assert.False(model.IsPreviewVisible);
        Assert.False(model.HasError);
        model.RefreshLanguage();
        model.PageIndex = (int)SettingsPage.PREVIEW;

        Assert.True(model.IsPreviewVisible);
        Assert.False(model.IsMediaVisible);
        Assert.True(model.HasError);
        Assert.Equal("0", model.Preview.SubtitleAuditionMillisecondsText);
    }
}
