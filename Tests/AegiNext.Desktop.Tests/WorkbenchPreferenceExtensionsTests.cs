using System.Collections.Immutable;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

public sealed class WorkbenchPreferenceExtensionsTests
{
    [Fact]
    public async Task ExistingPreferencesReceiveDefaultCommandsWithoutLosingAppearanceOrVolume()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "preferences.json"),
            "{\"Version\":1,\"Language\":\"en-US\",\"Theme\":2,\"Volume\":0.125}");
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var preferences = store.Load();
        Assert.Null(store.LoadError);
        Assert.Equal(0.125f, preferences.Volume);
        Assert.Equal("en-US", preferences.Language);
        Assert.Equal(WorkbenchTheme.DARK, preferences.Theme);
        Assert.Equal(ShortcutDefaults.CreateBindings().ToArray(), preferences.ShortcutBindings.ToArray());
        Assert.Equal("#5273E8", preferences.AccentColor);
    }

    [Fact]
    public async Task CustomBindingsAndAccentSurviveRestartWithStructuralEquality()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var bindings = ShortcutDefaults.CreateBindings().Select(binding => binding.Command == WorkbenchCommand.PLAY_PAUSE
            ? binding with { Gesture = "F5" } : binding).ToImmutableArray();
        var expected = new WorkbenchPreferences { AccentColor = "#D64A6C", ShortcutBindings = bindings };
        await store.SaveAsync(expected);
        using var reopened = new WorkbenchPreferencesStore(directory.Path);
        var actual = reopened.Load();
        Assert.Equal(expected, actual);
        Assert.Equal(expected.GetHashCode(), actual.GetHashCode());
        Assert.Equal("F5", actual.ShortcutBindings.Single(binding => binding.Command == WorkbenchCommand.PLAY_PAUSE).Gesture);
        Assert.NotEqual(expected, actual with { AccentColor = "#5273E8" });
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#GG0000")]
    [InlineData("#00112233")]
    [InlineData("#123")]
    public void InvalidAccentIsRejected(string color)
    {
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { AccentColor = color }.Validate());
    }

    [Fact]
    public async Task ConflictingAndIncompleteBindingsCannotReplaceValidPreferences()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        var original = new WorkbenchPreferences();
        await store.SaveAsync(original);
        var conflicted = original.ShortcutBindings.Select(binding => binding.Command == WorkbenchCommand.TIMING_ENTER
            ? binding with { Gesture = "Space" } : binding).ToImmutableArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(original with { ShortcutBindings = conflicted }));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.SaveAsync(original with { ShortcutBindings = [] }));
        Assert.Equal(original, store.Load());
        Assert.Single(Directory.GetFiles(directory.Path));
    }
}
