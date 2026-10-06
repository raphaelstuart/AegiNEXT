using AegiNext.Application.Presets;
using AegiNext.Core.Effects;

namespace AegiNext.Application.Tests.Presets;

/// <summary>脚本批量导入和所选快照导出的持久化合同。</summary>
public sealed class EffectScriptBatchImportTests
{
    private static readonly string[] expectedImportedNames = ["existing", "first", "second"];

    /// <summary>一次提交包含全部输入脚本，名称来自脚本标识。</summary>
    [Fact]
    public async Task MultipleFilesCommitAllScriptsAndCanBeReadBack()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.json");
        using var library = new EffectScriptPresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("existing"));
        var firstPath = Path.Combine(directory.Path, "first.aegifx");
        var secondPath = Path.Combine(directory.Path, "second.aegifx");
        await EffectScriptPresetStore.WriteScriptAsync(Preset("first").Source, firstPath);
        await EffectScriptPresetStore.WriteScriptAsync(Preset("second").Source, secondPath);

        await library.ImportAsync([firstPath, secondPath]);

        Assert.Equal(expectedImportedNames, library.Snapshot.Presets.Select(item => item.Name));
        var readBack = await EffectScriptPresetStore.LoadAsync(libraryPath);
        Assert.Equal(library.Snapshot.Presets.Select(item => item.Id), readBack.Presets.Select(item => item.Id));
    }

    /// <summary>后续非法脚本不允许之前已读取的脚本部分入库。</summary>
    [Fact]
    public async Task LaterInvalidFilePreservesSnapshotAndDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.json");
        using var library = new EffectScriptPresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(libraryPath);
        var firstPath = Path.Combine(directory.Path, "first.aegifx");
        var secondPath = Path.Combine(directory.Path, "invalid.aegifx");
        await EffectScriptPresetStore.WriteScriptAsync(Preset("valid").Source, firstPath);
        await File.WriteAllTextAsync(secondPath, "invalid source");

        await Assert.ThrowsAsync<EffectScriptException>(() => library.ImportAsync([firstPath, secondPath]));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(libraryPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    /// <summary>跨文件脚本标识重复或后续文件为保留内置脚本时整批拒绝。</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CrossFileConflictsPreserveSnapshotAndDisk(bool builtin)
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.json");
        using var library = new EffectScriptPresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(libraryPath);
        var firstPath = Path.Combine(directory.Path, "first.aegifx");
        var secondPath = Path.Combine(directory.Path, "second.aegifx");
        await EffectScriptPresetStore.WriteScriptAsync(Preset("imported").Source, firstPath);
        await EffectScriptPresetStore.WriteScriptAsync(builtin ? BuiltinEffectScripts.Get("fade-in").Source : Preset("imported").Source, secondPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.ImportAsync([firstPath, secondPath]));

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(libraryPath));
    }

    /// <summary>取消与空输入保留快照和磁盘内容。</summary>
    [Fact]
    public async Task CancellationAndEmptyBatchPreserveSnapshotAndDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.json");
        using var library = new EffectScriptPresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(libraryPath);
        var input = Path.Combine(directory.Path, "input.aegifx");
        await EffectScriptPresetStore.WriteScriptAsync(Preset("imported").Source, input);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.ImportAsync([input], cancellation.Token));
        await library.ImportAsync(Array.Empty<string>());

        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(libraryPath));
    }

    /// <summary>所选快照支持内置源和库外个人源，并禁止写回当前库。</summary>
    [Fact]
    public async Task SnapshotExportAllowsBuiltinAndPersonalSourcesWithoutChangingLibrary()
    {
        using var directory = new TemporaryProjectDirectory();
        var libraryPath = Path.Combine(directory.Path, "library.json");
        using var library = new EffectScriptPresetLibrary(libraryPath);
        await library.UpsertAsync(Preset("existing"));
        var snapshot = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(libraryPath);
        var builtin = new EffectScriptPreset(Guid.NewGuid(), "内置", BuiltinEffectScripts.Get("fade-in").Source);
        var personal = Preset("selected");
        var builtinPath = Path.Combine(directory.Path, "builtin.aegifx");
        var personalPath = Path.Combine(directory.Path, "personal.aegifx");

        await library.ExportAsync(builtin, builtinPath);
        await library.ExportAsync(personal, personalPath);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ExportAsync(builtin, libraryPath));
        await Assert.ThrowsAsync<EffectScriptException>(() => library.ExportAsync(personal with { Source = "invalid" }, Path.Combine(directory.Path, "invalid.aegifx")));

        Assert.Equal(builtin.Source, (await EffectScriptPresetStore.ReadScriptAsync(builtinPath)).Source);
        Assert.Equal(personal.Source, (await EffectScriptPresetStore.ReadScriptAsync(personalPath)).Source);
        Assert.Same(snapshot, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(libraryPath));
    }

    private static EffectScriptPreset Preset(string scriptId)
    {
        return new(Guid.NewGuid(), scriptId, BuiltinEffectScripts.Get("fade-in").Source.Replace("\"fade-in\"", $"\"{scriptId}\"", StringComparison.Ordinal));
    }
}
