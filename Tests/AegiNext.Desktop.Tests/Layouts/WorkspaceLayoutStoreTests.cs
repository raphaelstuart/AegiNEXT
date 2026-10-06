using AegiNext.Desktop.Layouts;

namespace AegiNext.Desktop.Tests.Layouts;

public sealed class WorkspaceLayoutStoreTests
{
    [Fact]
    public async Task CurrentLayoutWritesNeverOverwriteNamedPresets()
    {
        var directory = CreateDirectory();
        try
        {
            var store = new WorkspaceLayoutStore(directory);
            var original = WorkspaceLayoutPresets.Standard;
            var personal = new WorkspaceLayoutPreset("user-personal", "My workspace", false, original);
            await store.SaveAsync(new() { Current = original, CurrentPresetId = personal.Id, Presets = [personal] });
            var changed = WorkspaceLayoutPresets.BuiltIn.Single(preset => preset.Id == WorkspaceLayoutPresets.TIMING).Layout;
            await store.SaveAsync(new() { Current = changed, CurrentPresetId = personal.Id, Presets = [personal] });

            var restored = new WorkspaceLayoutStore(directory).Load();
            Assert.Equal(WorkspaceLayoutStore.Fingerprint(changed), WorkspaceLayoutStore.Fingerprint(restored.Current));
            Assert.Equal(WorkspaceLayoutStore.Fingerprint(original), WorkspaceLayoutStore.Fingerprint(Assert.Single(restored.Presets).Layout));
            Assert.Equal(personal.Id, restored.CurrentPresetId);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task QueuedAtomicWritesFlushTheLatestSnapshotAndLeaveNoTemporaryFiles()
    {
        var directory = CreateDirectory();
        try
        {
            var store = new WorkspaceLayoutStore(directory);
            var pending = new List<Task>();
            for (var index = 0; index < 20; index++)
            {
                var preset = new WorkspaceLayoutPreset("user-latest", "Layout " + index, false, WorkspaceLayoutPresets.Standard);
                pending.Add(store.SaveAsync(new() { CurrentPresetId = preset.Id, Presets = [preset] }));
            }
            await store.FlushAsync();
            await Task.WhenAll(pending);

            Assert.Equal("Layout 19", Assert.Single(store.Load().Presets).Name);
            Assert.Single(Directory.GetFiles(directory));
            Assert.Equal("layouts.json", Path.GetFileName(Assert.Single(Directory.GetFiles(directory))));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task FailedWriteDoesNotPoisonLaterWrites()
    {
        var directory = CreateDirectory();
        var blocker = Path.Combine(directory, "blocked");
        File.WriteAllText(blocker, "file");
        try
        {
            var store = new WorkspaceLayoutStore(blocker);
            await Assert.ThrowsAnyAsync<IOException>(() => store.SaveAsync(new()));
            File.Delete(blocker);
            await store.SaveAsync(new());
            await store.FlushAsync();
            Assert.True(File.Exists(Path.Combine(blocker, "layouts.json")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    [InlineData("{\"Version\":99}")]
    [InlineData("{\"Version\":1,\"Current\":null}")]
    public void DamagedFilesRetainDiagnosticsAndRestoreStandard(string content)
    {
        var directory = CreateDirectory();
        try
        {
            File.WriteAllText(Path.Combine(directory, "layouts.json"), content);
            var store = new WorkspaceLayoutStore(directory);
            var loaded = store.Load();

            Assert.Equal(WorkspaceLayoutPresets.STANDARD, loaded.CurrentPresetId);
            Assert.Equal(WorkspaceLayoutStore.Fingerprint(WorkspaceLayoutPresets.Standard), WorkspaceLayoutStore.Fingerprint(loaded.Current));
            Assert.NotNull(store.LoadError);
            Assert.NotNull(store.DiagnosticPath);
            Assert.Equal(content, File.ReadAllText(store.DiagnosticPath));
            Assert.True(File.Exists(store.DiagnosticPath + ".txt"));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task RenamingAndDeletingPersonalPresetsRoundTripWithoutChangingCurrentTopology()
    {
        var directory = CreateDirectory();
        try
        {
            var store = new WorkspaceLayoutStore(directory);
            var layout = WorkspaceLayoutPresets.BuiltIn.Single(preset => preset.Id == WorkspaceLayoutPresets.EFFECTS).Layout;
            var preset = new WorkspaceLayoutPreset("user-personal", "Before", false, layout);
            await store.SaveAsync(new() { Current = layout, CurrentPresetId = preset.Id, Presets = [preset] });
            await store.SaveAsync(store.Load() with { Presets = [preset with { Name = "After" }] });
            Assert.Equal("After", Assert.Single(store.Load().Presets).Name);
            await store.SaveAsync(store.Load() with { CurrentPresetId = WorkspaceLayoutPresets.STANDARD, Presets = [] });

            Assert.Empty(store.Load().Presets);
            Assert.Equal(WorkspaceLayoutStore.Fingerprint(layout), WorkspaceLayoutStore.Fingerprint(store.Load().Current));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreateDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext.LayoutTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
