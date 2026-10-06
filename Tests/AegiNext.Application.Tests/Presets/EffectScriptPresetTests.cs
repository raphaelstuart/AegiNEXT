using System.Text;
using AegiNext.Application.Presets;
using AegiNext.Core.Effects;

namespace AegiNext.Application.Tests.Presets;

public sealed class EffectScriptPresetTests
{
    [Fact]
    public async Task PersonalLibraryAndSourceExchangeRoundTripWithoutProjectState()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effect-scripts.json");
        var preset = Preset("custom-fade", "自定义淡入");
        using (var library = new EffectScriptPresetLibrary(path))
        {
            await library.LoadAsync();
            Assert.Empty(library.Snapshot.Presets);
            await library.UpsertAsync(preset);
            Assert.Equal(preset, Assert.Single(library.Snapshot.Presets));
            var sourcePath = Path.Combine(directory.Path, "空格 脚本.aegifx");
            await library.ExportAsync(preset.Id, sourcePath);
            var exported = await EffectScriptPresetStore.ReadScriptAsync(sourcePath);
            Assert.Equal(preset.Source, exported.Source);
            Assert.Equal("custom-fade", exported.Script.Id);
            var importPath = Path.Combine(directory.Path, "另一个.aegifx");
            await EffectScriptPresetStore.WriteScriptAsync(preset.Source.Replace("custom-fade", "second-fade", StringComparison.Ordinal), importPath);
            await library.ImportAsync(importPath);
            Assert.Equal(2, library.Snapshot.Presets.Length);
        }

        using var loaded = new EffectScriptPresetLibrary(path);
        await loaded.LoadAsync();
        Assert.Equal(2, loaded.Snapshot.Presets.Length);
        Assert.Contains(loaded.Snapshot.Presets, item => item == preset);
        await loaded.RemoveAsync(preset.Id);
        Assert.Single(loaded.Snapshot.Presets);
    }

    [Fact]
    public async Task InvalidConflictCanceledAndFailedWritesKeepSnapshotAndDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effects.json");
        using var library = new EffectScriptPresetLibrary(path);
        var preset = Preset("my-fade", "保留名称");
        await library.UpsertAsync(preset);
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        await Assert.ThrowsAsync<EffectScriptException>(() => library.UpsertAsync(preset with { Source = "broken source" }));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.UpsertAsync(Preset("my-fade", "同脚本标识")));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.UpsertAsync(Preset("different-fade", "保留名称")));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.UpsertAsync(Preset("cancel-fade", "取消"), cancellation.Token));
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Theory]
    [InlineData("{\"version\":1,\"version\":1,\"presets\":[]}")]
    [InlineData("{\"version\":99,\"presets\":[]}")]
    [InlineData("{\"version\":1,\"presets\":[],\"unknown\":0}")]
    [InlineData("{\"presets\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("[]")]
    [InlineData("{\"version\":1,\"presets\":[null]}")]
    public async Task InvalidLibraryNeverReplacesTheLastSuccessfulSnapshot(string json)
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effects.json");
        using var library = new EffectScriptPresetLibrary(path);
        await library.UpsertAsync(Preset("retained-fade", "保留"));
        var snapshot = library.Snapshot;
        await File.WriteAllTextAsync(path, json);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.LoadAsync());
        Assert.Same(snapshot, library.Snapshot);
    }

    [Fact]
    public async Task ImportRejectsInvalidUtf8BuiltinsAndDuplicatesAtomically()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "effects.json");
        using var library = new EffectScriptPresetLibrary(path);
        await library.UpsertAsync(Preset("personal-fade", "个人"));
        var before = await File.ReadAllBytesAsync(path);
        var sourcePath = Path.Combine(directory.Path, "bad.aegifx");
        await File.WriteAllBytesAsync(sourcePath, [0xFF, 0xFF]);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(sourcePath));
        await File.WriteAllTextAsync(sourcePath, BuiltinEffectScripts.Get("fade-in").Source, Encoding.UTF8);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(sourcePath));
        await File.WriteAllTextAsync(sourcePath, Preset("personal-fade", "重复").Source);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync(sourcePath));
        Assert.Equal(before, await File.ReadAllBytesAsync(path));
        Assert.Single(library.Snapshot.Presets);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(library.Snapshot.Presets[0].Id, path));
    }

    [Fact]
    public async Task ConcurrentCommitsDoNotLoseTemplatesAndDisposalRejectsNewWork()
    {
        using var directory = new TemporaryProjectDirectory();
        var library = new EffectScriptPresetLibrary(Path.Combine(directory.Path, "effects.json"));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(index => library.UpsertAsync(Preset($"fade-{index}", $"模板{index}"))));
        Assert.Equal(8, library.Snapshot.Presets.Length);
        library.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => library.LoadAsync());
    }

    private static EffectScriptPreset Preset(string scriptId, string name)
    {
        return new(Guid.NewGuid(), name, BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"{scriptId}\"", StringComparison.Ordinal));
    }
}
