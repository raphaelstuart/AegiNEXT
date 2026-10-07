using AegiNext.Application.Presets;
using AegiNext.Core.Effects;

namespace AegiNext.Application.Tests.Presets;

public sealed class EffectScriptPresetLibraryBatchDeleteTests
{
    [Fact]
    public async Task BatchDeleteFixesAndDeduplicatesIdsWhilePreservingRemainingOrder()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effects.json");
        using var library = new EffectScriptPresetLibrary(path);
        var first = Preset("First");
        var second = Preset("Second");
        var third = Preset("Third");
        var fourth = Preset("Fourth");
        foreach (var preset in new[] { first, second, third, fourth })
        {
            await library.UpsertAsync(preset);
        }
        var ids = new List<Guid> { third.Id, first.Id, third.Id };

        var deletion = library.RemoveAsync(ids);
        ids.Clear();
        ids.Add(second.Id);
        await deletion;

        Assert.Equal(new[] { second, fourth }, library.Snapshot.Presets.ToArray());
        Assert.Equal(new[] { second, fourth }, (await EffectScriptPresetStore.LoadAsync(path)).Presets.ToArray());
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task UnknownIdAndCancellationRejectTheWholeBatchWithoutChangingSnapshotOrDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effects.json");
        using var library = new EffectScriptPresetLibrary(path);
        var first = Preset("First");
        var second = Preset("Second");
        await library.UpsertAsync(first);
        await library.UpsertAsync(second);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.RemoveAsync([first.Id, Guid.NewGuid(), second.Id]));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.RemoveAsync([first.Id, second.Id], cancellation.Token));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task EmptyBatchDoesNotLoadOrWriteAnyLibraryFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "nested", "effects.json");
        using var library = new EffectScriptPresetLibrary(path);
        var snapshot = library.Snapshot;

        await library.RemoveAsync(Array.Empty<Guid>());

        Assert.Same(snapshot, library.Snapshot);
        Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "Invalid library");
        await library.RemoveAsync(Array.Empty<Guid>());
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal("Invalid library", await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task FailedBatchCommitKeepsPublishedSnapshotAndCleansTemporaryFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effects.json");
        using var library = new EffectScriptPresetLibrary(path);
        var first = Preset("First");
        var second = Preset("Second");
        await library.UpsertAsync(first);
        await library.UpsertAsync(second);
        var snapshot = library.Snapshot;
        File.Delete(path);
        Directory.CreateDirectory(path);

        await Assert.ThrowsAsync<IOException>(() => library.RemoveAsync([first.Id, second.Id]));

        Assert.Same(snapshot, library.Snapshot);
        Assert.True(Directory.Exists(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task FirstDeletionLoadsTheExistingLibraryAndCanRemoveTheEntireSelection()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effects.json");
        var first = Preset("First");
        var second = Preset("Second");
        await EffectScriptPresetStore.SaveAsync(new() { Presets = [first, second] }, path);
        using var library = new EffectScriptPresetLibrary(path);

        await library.RemoveAsync([first.Id, second.Id]);

        Assert.Empty(library.Snapshot.Presets);
        Assert.Empty((await EffectScriptPresetStore.LoadAsync(path)).Presets);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        await library.RemoveAsync(Array.Empty<Guid>());
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }

    private static EffectScriptPreset Preset(string name)
    {
        var id = Guid.NewGuid();
        var source = BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"custom-{id:N}\"", StringComparison.Ordinal);
        return new(id, name, source);
    }
}
