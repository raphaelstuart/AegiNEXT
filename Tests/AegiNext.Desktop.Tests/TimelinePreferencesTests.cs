using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Tests;

public sealed class TimelinePreferencesTests
{
    [Fact]
    public async Task MissingTimelineOptionsKeepExistingDefaultsWithoutRewritingVersionOneSettings()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        const string JSON = "{\"Version\":1,\"Language\":\"zh-CN\",\"Volume\":0.375}";
        await File.WriteAllTextAsync(path, JSON, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var preferences = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(1, preferences.Version);
        Assert.True(preferences.TimelineSnapEnabled);
        Assert.False(preferences.TimelineStepEnabled);
        Assert.True(preferences.TimelineSpectrumVisible);
        Assert.True(preferences.TimelineWaveformVisible);
        Assert.Equal("zh-CN", preferences.Language);
        Assert.Equal(0.375f, preferences.Volume);
        Assert.Equal(JSON, await File.ReadAllTextAsync(path, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public async Task EveryTimelineCombinationRoundTripsWithUnrelatedSettings(int combination)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var expected = new WorkbenchPreferences
        {
            TimelineSnapEnabled = (combination & 1) != 0,
            TimelineStepEnabled = (combination & 2) != 0,
            TimelineSpectrumVisible = (combination & 4) != 0,
            TimelineWaveformVisible = (combination & 8) != 0,
            Language = "zh-CN",
            Volume = 0.375f,
            PreviewQuality = PreviewQuality.HIGH
        };

        await store.SaveAsync(expected, CancellationToken.None);
        using var reopened = new WorkbenchPreferencesStore(directory.Path);
        var restored = reopened.Load();

        Assert.Null(reopened.LoadError);
        Assert.Equal(expected, restored);
        Assert.Equal(expected.TimelineSnapEnabled, restored.TimelineSnapEnabled);
        Assert.Equal(expected.TimelineStepEnabled, restored.TimelineStepEnabled);
        Assert.Equal(expected.TimelineSpectrumVisible, restored.TimelineSpectrumVisible);
        Assert.Equal(expected.TimelineWaveformVisible, restored.TimelineWaveformVisible);
        Assert.Equal(1, restored.Version);
        Assert.Equal(expected.Language, restored.Language);
        Assert.Equal(expected.Volume, restored.Volume);
        Assert.Equal(expected.PreviewQuality, restored.PreviewQuality);
    }

    [Fact]
    public void EachTimelineOptionParticipatesInPreferenceEqualityAndEqualCopiesKeepTheirHash()
    {
        var original = new WorkbenchPreferences();
        var changes = new[]
        {
            original with { TimelineSnapEnabled = false },
            original with { TimelineStepEnabled = true },
            original with { TimelineSpectrumVisible = false },
            original with { TimelineWaveformVisible = false }
        };

        foreach (var changed in changes)
        {
            Assert.NotEqual(original, changed);
            Assert.False(original == changed);
            var copy = changed with { };
            Assert.Equal(changed, copy);
            Assert.Equal(changed.GetHashCode(), copy.GetHashCode());
        }
    }
}
