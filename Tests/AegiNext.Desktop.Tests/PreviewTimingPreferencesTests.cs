using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Tests;

public sealed class PreviewTimingPreferencesTests
{
    [Fact]
    public async Task MissingTimingPreferencesKeepTheOriginalAuditionLengthWithoutRewritingTheFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Language\":\"zh-CN\",\"Volume\":0.375}";
        await File.WriteAllTextAsync(path, JSON, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(500, preferences.SubtitleAuditionMilliseconds);
        Assert.False(preferences.TimelineClassicTimingEnabled);
        Assert.Equal("zh-CN", preferences.Language);
        Assert.Equal(0.375f, preferences.Volume);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(750, true)]
    [InlineData(60001, false)]
    [InlineData(int.MaxValue, true)]
    public async Task PositiveIntMillisecondsAndClassicTimingPersistWithUnrelatedSettings(int milliseconds, bool classic)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences
        {
            SubtitleAuditionMilliseconds = milliseconds, TimelineClassicTimingEnabled = classic,
            Theme = WorkbenchTheme.DARK, Language = "zh-CN", Volume = 0.375f
        };

        await store.SaveAsync(expected, CancellationToken.None);

        using var reopened = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(expected, reopened.Load());
        Assert.Null(reopened.LoadError);
        Assert.NotEqual(expected, expected with { TimelineClassicTimingEnabled = !classic });
        Assert.NotEqual(expected, expected with { SubtitleAuditionMilliseconds = milliseconds == 1 ? 2 : 1 });
        Assert.Equal(expected.GetHashCode(), reopened.Load().GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public async Task NonpositiveMillisecondsAreRejectedBeforeReplacingSavedPreferences(int milliseconds)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var original = new WorkbenchPreferences { SubtitleAuditionMilliseconds = 750 };
        await store.SaveAsync(original, CancellationToken.None);
        var path = Path.Combine(directory.Path, "preferences.json");
        var bytes = await File.ReadAllBytesAsync(path, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.SaveAsync(original with { SubtitleAuditionMilliseconds = milliseconds }, CancellationToken.None));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, CancellationToken.None));
        Assert.Equal(original, store.Load());
    }
}
