using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Tests;

public sealed class PreviewQualityPreferencesTests
{
    [Fact]
    public async Task PreviousPreferencesDefaultToLowQualityWithoutRewritingTheFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Language\":\"en-US\",\"Volume\":0.25}";
        await File.WriteAllTextAsync(path, JSON, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var preferences = store.Load();
        Assert.Equal(PreviewQuality.LOW, preferences.PreviewQuality);
        Assert.Equal(0.25f, preferences.Volume);
        Assert.Null(store.LoadError);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData(PreviewQuality.LOWEST)]
    [InlineData(PreviewQuality.LOW)]
    [InlineData(PreviewQuality.STANDARD)]
    [InlineData(PreviewQuality.HIGH)]
    public async Task QualityPersistsIndependentlyAndParticipatesInPreferenceEquality(PreviewQuality quality)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences { PreviewQuality = quality, Language = "zh-CN", Volume = 0.42f };
        Assert.NotEqual(expected, expected with { PreviewQuality = (PreviewQuality)99 });
        await store.SaveAsync(expected, CancellationToken.None);
        using var reloadedStore = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(expected, reloadedStore.Load());
        Assert.Null(reloadedStore.LoadError);
    }

    [Theory]
    [InlineData(0, PreviewQuality.LOW)]
    [InlineData(1, PreviewQuality.STANDARD)]
    [InlineData(2, PreviewQuality.HIGH)]
    public async Task ExistingSavedQualityValuesKeepTheirMeaning(int savedValue, PreviewQuality expected)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        var json = $$"""{"PreviewQuality":{{savedValue}},"Language":"en-US","Volume":0.25}""";
        await File.WriteAllTextAsync(path, json, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        Assert.Equal(expected, store.Load().PreviewQuality);
        Assert.Null(store.LoadError);
        Assert.Equal(json, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Fact]
    public void UnknownQualityIsRejected()
    {
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { PreviewQuality = (PreviewQuality)99 }.Validate());
    }
}
