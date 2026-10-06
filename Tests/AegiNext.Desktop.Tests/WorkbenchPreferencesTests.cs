using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;
using System.Collections.Immutable;
using System.Text.Json;

namespace AegiNext.Desktop.Tests;

public sealed class WorkbenchPreferencesTests
{
    [Fact]
    public async Task PreferencesRoundTripLanguageThemeAndVolumeInInjectedDirectory()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(new(), store.Load());
        Assert.Null(store.LoadError);
        Assert.Empty(Directory.GetFiles(directory.Path));

        var expected = new WorkbenchPreferences { Language = "zh-CN", Theme = WorkbenchTheme.DARK, Volume = 0.25f, WindowMenuOnMac = true };
        await store.SaveAsync(expected);
        Assert.Equal(expected, store.Load());
        using var reopened = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(expected, reopened.Load());
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task CancellationAndValidationFailurePreserveExistingSettingsWithoutTemporaryFiles()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var original = new WorkbenchPreferences { Language = "en-US", Theme = WorkbenchTheme.LIGHT, Volume = 0.5f };
        await store.SaveAsync(original);
        var path = Path.Combine(directory.Path, "preferences.json");
        var bytes = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(original with { Volume = 0 }, cancellation.Token));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(original with { Volume = float.NaN }, CancellationToken.None));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, CancellationToken.None));
        Assert.Equal(original, store.Load());
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Fact]
    public async Task ConcurrentSavesAreWholeDocumentsAndLatestQueuedSaveWins()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var first = new WorkbenchPreferences { Language = "zh-CN", Theme = WorkbenchTheme.DARK, Volume = 0.125f };
        var second = new WorkbenchPreferences { Language = "en-US", Theme = WorkbenchTheme.LIGHT, Volume = 0.875f };
        await Task.WhenAll(store.SaveAsync(first), store.SaveAsync(second));
        Assert.Equal(second, store.Load());
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData("{bad")]
    [InlineData("null")]
    [InlineData("{\"Version\":2}")]
    [InlineData("{\"Language\":\"!!!\"}")]
    [InlineData("{\"Theme\":99}")]
    [InlineData("{\"Volume\":-1}")]
    public async Task CorruptPreferencesRetainDiagnosticAndFileUntilExplicitSave(string json)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        await File.WriteAllTextAsync(path, json);
        using var store = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(new(), store.Load());
        Assert.NotNull(store.LoadError);
        Assert.Equal(json, await File.ReadAllTextAsync(path));

        await store.SaveAsync(new() { Language = "en-US" });
        Assert.Equal("en-US", store.Load().Language);
        Assert.Null(store.LoadError);
    }

    [Fact]
    public async Task OversizedPreferencesFallBackWithDiagnosticWithoutRemovingFile()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var path = Path.Combine(directory.Path, "preferences.json");
        await File.WriteAllTextAsync(path, new string(' ', 65537));
        using var store = new WorkbenchPreferencesStore(directory.Path);
        Assert.Equal(new(), store.Load());
        Assert.IsType<InvalidDataException>(store.LoadError);
        Assert.Equal(65537, new FileInfo(path).Length);
    }

    /// <summary>A valid language can be persisted while its optional package is unavailable.</summary>
    [Theory]
    [InlineData("ja-JP")]
    [InlineData("fr-FR")]
    public async Task UninstalledLanguagePreservesAllPreferencesAndDoesNotRequireMigration(string languageID)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var preferences = new WorkbenchPreferences
        {
            Language = languageID,
            Theme = WorkbenchTheme.DARK,
            Volume = 0.375f,
            WindowMenuOnMac = true
        };

        await store.SaveAsync(preferences);

        Assert.Equal(preferences, store.Load());
        Assert.Null(store.LoadError);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(-0.001f)]
    [InlineData(1.001f)]
    public void InvalidVolumeIsRejectedBeforePersistence(float volume)
    {
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { Volume = volume }.Validate());
    }

    [Fact]
    public async Task PreviousCommandSetUpgradesWithoutChangingCustomizedOrDisabledGestures()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var previous = ShortcutDefaults.CreateBindings()
            .Where(value => value.Command <= WorkbenchCommand.VIEW_TIMELINE)
            .Select(value => value.Command switch
            {
                WorkbenchCommand.NEW_PROJECT => value with { Gesture = "F6" },
                WorkbenchCommand.OPEN_PROJECT => value with { Gesture = string.Empty },
                _ => value
            }).ToImmutableArray();
        var original = new WorkbenchPreferences { ShortcutBindings = previous, Language = "zh-CN", Volume = 0.375f };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(original);
        var path = Path.Combine(directory.Path, "preferences.json");
        await File.WriteAllBytesAsync(path, bytes);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var upgraded = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(previous, upgraded.ShortcutBindings.Take(previous.Length));
        Assert.Equal(Enum.GetValues<WorkbenchCommand>().Length, upgraded.ShortcutBindings.Length);
        Assert.All(upgraded.ShortcutBindings.Skip(previous.Length), value => Assert.Equal(
            ShortcutDefaults.CreateBindings().Single(binding => binding.Command == value.Command).Gesture, value.Gesture));
        Assert.Equal(original.Language, upgraded.Language);
        Assert.Equal(original.Volume, upgraded.Volume);
        Assert.False(upgraded.WindowMenuOnMac);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await store.SaveAsync(upgraded);
        Assert.Equal(upgraded, store.Load());
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("conflict")]
    [InlineData("missing")]
    public async Task MigrationRejectsCorruptOldBindingsAndPreservesDiagnostics(string corruption)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var previous = ShortcutDefaults.CreateBindings().Where(value => value.Command <= WorkbenchCommand.VIEW_TIMELINE).ToArray();
        previous = corruption switch
        {
            "unknown" => [new((WorkbenchCommand)999, "F6"), .. previous.Skip(1)],
            "duplicate" => [previous[1], .. previous.Skip(1)],
            "conflict" => [previous[0] with { Gesture = previous[1].Gesture }, .. previous.Skip(1)],
            _ => [.. previous.Skip(1)]
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new WorkbenchPreferences { ShortcutBindings = [.. previous] });
        var path = Path.Combine(directory.Path, "preferences.json");
        await File.WriteAllBytesAsync(path, bytes);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        Assert.Equal(new(), store.Load());
        Assert.IsType<InvalidDataException>(store.LoadError);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }
}
