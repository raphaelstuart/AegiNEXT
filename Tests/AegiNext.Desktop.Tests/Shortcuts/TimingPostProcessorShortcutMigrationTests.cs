using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class TimingPostProcessorShortcutMigrationTests
{
    [Fact]
    public async Task PreviousCompleteCommandSetPreservesPreferencesAndAddsAnUnboundProcessorCommand()
    {
        var previous = new WorkbenchPreferences
        {
            Language = "zh-CN",
            ShortcutBindings = ShortcutDefaults.CreateBindings()
                .Where(binding => binding.Command != WorkbenchCommand.APPLY_TIMING_POST_PROCESSOR)
                .Select(binding => binding.Command == WorkbenchCommand.TIMING_ENTER
                    ? binding with { Gesture = "F6" } : binding)
                .ToImmutableArray()
        };

        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        await File.WriteAllBytesAsync(Path.Combine(directory.Path, "preferences.json"),
            JsonSerializer.SerializeToUtf8Bytes(previous));
        var loaded = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(previous.Language, loaded.Language);
        Assert.Equal("F6", loaded.ShortcutBindings.Single(binding => binding.Command == WorkbenchCommand.TIMING_ENTER).Gesture);
        Assert.Equal(string.Empty, loaded.ShortcutBindings.Single(binding =>
            binding.Command == WorkbenchCommand.APPLY_TIMING_POST_PROCESSOR).Gesture);
        Assert.Equal(previous.ShortcutBindings.AsEnumerable(), loaded.ShortcutBindings.Where(binding =>
            binding.Command != WorkbenchCommand.APPLY_TIMING_POST_PROCESSOR));
        loaded.Validate();
    }
}
