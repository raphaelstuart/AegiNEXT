using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using AegiNext.Application.Presets;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests.Presets;

public sealed class SubtitleStyleTimingBindingTests
{
    [Fact]
    public async Task BatchBindingAndUnbindingPublishCompletePresetsAndPreserveTheirContent()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var first = Preset("中文样式");
        var second = Preset("Dialogue");
        var excluded = Preset("Excluded");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [first, second, excluded] }, path);
        using var library = new SubtitleStylePresetLibrary(path);
        await library.LoadAsync();
        var before = library.Snapshot;
        var options = new TimingPostProcessorOptions { LeadInMilliseconds = 120, BiasPercent = 40 };

        await library.SetTimingPostProcessorAsync([first.Id, second.Id, first.Id], options);

        var after = library.Snapshot;
        Assert.NotSame(before, after);
        Assert.Equal(first with { TimingPostProcessor = options }, after.Presets[0]);
        Assert.Equal(second with { TimingPostProcessor = options }, after.Presets[1]);
        Assert.Same(before.Presets[2], after.Presets[2]);
        Assert.Null(before.Presets[0].TimingPostProcessor);
        var persisted = await SubtitleStylePresetStore.LoadAsync(path);
        Assert.Equal(after.Presets.ToArray(), persisted.Presets.ToArray());

        await library.SetTimingPostProcessorAsync([first.Id, second.Id], null);

        Assert.All(library.Snapshot.Presets, preset => Assert.Null(preset.TimingPostProcessor));
        Assert.Equal(first, library.Snapshot.Presets[0]);
        Assert.Equal(second, library.Snapshot.Presets[1]);
    }

    [Fact]
    public async Task UnknownIdsInvalidOptionsAndCancellationLeaveTheEntireLibraryUnchanged()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var first = Preset("First");
        using var library = new SubtitleStylePresetLibrary(path);
        await library.UpsertAsync(first);
        var before = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<InvalidDataException>(() => library.SetTimingPostProcessorAsync(
            [first.Id, Guid.NewGuid()], new()));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.SetTimingPostProcessorAsync(
            [first.Id], new() { EndBeforeMilliseconds = -1 }));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => library.SetTimingPostProcessorAsync(
            [first.Id], new(), cancellation.Token));

        Assert.Same(before, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task FailedAtomicReplacementDoesNotPublishAnyBinding()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var originalPath = Path.Combine(directory.Path, "original.aegistyles");
        var first = Preset("First");
        var second = Preset("Second");
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = [first, second] }, path);
        using var library = new SubtitleStylePresetLibrary(path);
        await library.LoadAsync();
        var before = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        File.Move(path, originalPath);
        Directory.CreateDirectory(path);

        await Assert.ThrowsAnyAsync<IOException>(() => library.SetTimingPostProcessorAsync([first.Id, second.Id], new()));

        Assert.Same(before, library.Snapshot);
        Assert.All(library.Snapshot.Presets, preset => Assert.Null(preset.TimingPostProcessor));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(originalPath));
        Assert.Empty(Directory.GetFiles(directory.Path, "*.tmp"));
    }

    [Fact]
    public async Task IdenticalOrEmptyBindingDoesNotWriteOrPublish()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var options = new TimingPostProcessorOptions();
        var preset = Preset("Bound") with { TimingPostProcessor = options };
        using var library = new SubtitleStylePresetLibrary(path);
        await library.UpsertAsync(preset);
        var before = library.Snapshot;
        var timestamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(path, timestamp);

        await library.SetTimingPostProcessorAsync([preset.Id], options with { });
        await library.SetTimingPostProcessorAsync([], null);

        Assert.Same(before, library.Snapshot);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(path));
        using var empty = new SubtitleStylePresetLibrary(Path.Combine(directory.Path, "empty.aegistyles"));
        await empty.SetTimingPostProcessorAsync([], null);
        Assert.False(File.Exists(Path.Combine(directory.Path, "empty.aegistyles")));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void LegacyLibrariesUpgradeWithoutInventingAnAssociation(int version)
    {
        var json = JsonNode.Parse(SubtitleStylePresetStore.Serialize(new() { Presets = [Preset("Legacy")] }))!.AsObject();
        json["version"] = version;
        var preset = json["presets"]![0]!.AsObject();
        preset.Remove("timingPostProcessor");
        var style = preset["style"]!.AsObject();
        if (version == 1)
        {
            style.Remove("position");
        }
        if (version <= 2)
        {
            style.Remove("underline");
            style.Remove("strikethrough");
        }

        var restored = SubtitleStylePresetStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(4, restored.Version);
        Assert.Null(Assert.Single(restored.Presets).TimingPostProcessor);
    }

    [Theory]
    [InlineData("leadInEnabled")]
    [InlineData("leadInMilliseconds")]
    [InlineData("leadOutEnabled")]
    [InlineData("leadOutMilliseconds")]
    [InlineData("adjacencyEnabled")]
    [InlineData("maximumGapMilliseconds")]
    [InlineData("maximumOverlapMilliseconds")]
    [InlineData("biasPercent")]
    [InlineData("keyframeSnapEnabled")]
    [InlineData("startBeforeMilliseconds")]
    [InlineData("startAfterMilliseconds")]
    [InlineData("endBeforeMilliseconds")]
    [InlineData("endAfterMilliseconds")]
    public void PresentOptionsRequireEveryField(string property)
    {
        var json = BoundJson();
        json["presets"]![0]!["timingPostProcessor"]!.AsObject().Remove(property);

        Assert.Throws<InvalidDataException>(() => Deserialize(json));
    }

    [Fact]
    public void OptionsRejectUnknownFieldsAndInvalidValuesWhileMissingAssociationIsOptional()
    {
        var json = BoundJson();
        json["presets"]![0]!["timingPostProcessor"]!["unknown"] = true;
        Assert.Throws<InvalidDataException>(() => Deserialize(json));
        json = BoundJson();
        json["presets"]![0]!["timingPostProcessor"]!["biasPercent"] = 101;
        Assert.Throws<InvalidDataException>(() => Deserialize(json));
        json = BoundJson();
        json["presets"]![0]!.AsObject().Remove("timingPostProcessor");
        Assert.Null(Assert.Single(Deserialize(json).Presets).TimingPostProcessor);
    }

    [Fact]
    public async Task PortableAssociationsSurviveExportImportRenameVisualEditDuplicateAndDelete()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var options = new TimingPostProcessorOptions { LeadOutMilliseconds = 222, KeyframeSnapEnabled = false };
        var preset = Preset("Original") with { TimingPostProcessor = options };
        using var library = new SubtitleStylePresetLibrary(path);
        await library.UpsertAsync(preset);
        var changed = preset with { Name = "Renamed", Style = preset.Style with { FontSize = 72 } };
        await library.UpsertAsync(changed);
        var duplicate = changed with { Id = Guid.NewGuid(), Name = "Duplicate" };
        await library.UpsertAsync(duplicate);
        var exportPath = Path.Combine(directory.Path, "export.aegistyles");
        await library.ExportAsync(exportPath, [duplicate.Id]);
        using var imported = new SubtitleStylePresetLibrary(Path.Combine(directory.Path, "imported.aegistyles"));
        await imported.ImportAsync(exportPath);

        Assert.Equal(duplicate, Assert.Single(imported.Snapshot.Presets));
        Assert.Equal(options, library.Snapshot.Presets[0].TimingPostProcessor);
        Assert.Equal(options, library.Snapshot.Presets[1].TimingPostProcessor);
        await library.RemoveAsync(preset.Id);
        Assert.Equal(duplicate.Id, Assert.Single(library.Snapshot.Presets).Id);
        Assert.Equal(options, Assert.Single((await SubtitleStylePresetStore.LoadAsync(path)).Presets).TimingPostProcessor);
        Assert.Equal(4, (await SubtitleStylePresetStore.LoadAsync(exportPath)).Version);
    }

    [Fact]
    public async Task BindingPreservesEmbeddedFontResourcesInMemoryAndOnDisk()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var bytes = ImmutableArray.Create<byte>(1, 2, 3, 4);
        var font = new EmbeddedSubtitleFont("font.ttf", Convert.ToHexStringLower(SHA256.HashData(bytes.AsSpan())), bytes);
        var preset = Preset("Embedded font") with { Font = font };
        using var library = new SubtitleStylePresetLibrary(path);
        await library.UpsertAsync(preset);

        await library.SetTimingPostProcessorAsync([preset.Id], new());

        var bound = Assert.Single(library.Snapshot.Presets);
        Assert.Equal(preset.Id, bound.Id);
        Assert.Same(preset.Style, bound.Style);
        Assert.Same(font, bound.Font);
        var restored = Assert.Single((await SubtitleStylePresetStore.LoadAsync(path)).Presets);
        Assert.Equal(font.Sha256, restored.Font!.Sha256);
        Assert.Equal(bytes.ToArray(), restored.Font.Data.ToArray());
        Assert.Equal(bound.TimingPostProcessor, restored.TimingPostProcessor);
    }

    [Fact]
    public async Task BindingAnExistingFullLibraryLoadsItAndDoesNotConsumePresetCapacity()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "styles.aegistyles");
        var presets = Enumerable.Range(0, 256).Select(index => Preset($"Style {index}")).ToImmutableArray();
        await SubtitleStylePresetStore.SaveAsync(new() { Presets = presets }, path);
        using var library = new SubtitleStylePresetLibrary(path);

        await library.SetTimingPostProcessorAsync(presets.Select(preset => preset.Id), new());

        Assert.Equal(256, library.Snapshot.Presets.Length);
        Assert.All(library.Snapshot.Presets, preset => Assert.NotNull(preset.TimingPostProcessor));
        Assert.Equal(presets.Select(preset => preset.Id), library.Snapshot.Presets.Select(preset => preset.Id));
    }

    private static SubtitleStylePreset Preset(string name)
    {
        return new(Guid.NewGuid(), name, new SubtitleStyle { FontFamily = "Arial", FontSize = 64 });
    }

    private static JsonObject BoundJson()
    {
        var preset = Preset("Bound") with { TimingPostProcessor = new() };
        return JsonNode.Parse(SubtitleStylePresetStore.Serialize(new() { Presets = [preset] }))!.AsObject();
    }

    private static SubtitleStylePresetCollection Deserialize(JsonObject json)
    {
        return SubtitleStylePresetStore.Deserialize(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString()));
    }
}
