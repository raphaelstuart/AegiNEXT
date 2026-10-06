using System.Globalization;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectBackupStoreTests
{
    private static readonly DateTimeOffset timestamp = new(2026, 10, 6, 10, 20, 30, 123, TimeSpan.FromHours(8));

    [Fact]
    public async Task BackupPreservesRawReferencesWithoutChangingMainFileOrEditorSavePoint()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "工程.aeginext");
        await ProjectStore.SaveAsync(new() { Name = "On disk" }, path);
        var original = await File.ReadAllBytesAsync(path);
        var editor = new ProjectEditor();
        editor.Apply("References", document => document with
        {
            Assets =
            [
                new(Guid.NewGuid(), ProjectAssetKind.MEDIA, "media/video.mkv"),
                new(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/font.ttf"),
                new(Guid.NewGuid(), ProjectAssetKind.IMAGE, "assets/image.png")
            ]
        });
        var snapshot = editor.Snapshot;
        var undoLabel = editor.UndoLabel;

        var backup = await ProjectBackupStore.WriteAsync(snapshot, path, timestamp, 5);

        Assert.Equal(ProjectStore.Serialize(snapshot), await File.ReadAllBytesAsync(backup));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Same(snapshot, editor.Snapshot);
        Assert.True(editor.HasUnsavedChanges);
        Assert.Equal(undoLabel, editor.UndoLabel);
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.Combine(directory.Path, "backup")));
        Assert.EndsWith("工程-" + timestamp.ToLocalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + ".aeginext", backup);
    }

    [Fact]
    public async Task CollidingTimestampAdvancesOneMillisecondWithoutOverwritingBackup()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        var first = await ProjectBackupStore.WriteAsync(new() { Name = "First" }, path, timestamp, 5);
        var second = await ProjectBackupStore.WriteAsync(new() { Name = "Second" }, path, timestamp, 5);

        Assert.NotEqual(first, second);
        Assert.Equal("First", (await ProjectStore.LoadAsync(first)).Name);
        Assert.Equal("Second", (await ProjectStore.LoadAsync(second)).Name);
        Assert.EndsWith(timestamp.AddMilliseconds(1).ToLocalTime().ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + ".aeginext", second);
    }

    [Fact]
    public async Task PruningOnlyDeletesOldestValidTimestampFilesForExactProjectBaseName()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        var otherPath = Path.Combine(directory.Path, "project-other.aeginext");
        var first = await ProjectBackupStore.WriteAsync(new(), path, timestamp, 10);
        var second = await ProjectBackupStore.WriteAsync(new(), path, timestamp.AddSeconds(1), 10);
        var other = await ProjectBackupStore.WriteAsync(new(), otherPath, timestamp, 10);
        var backupDirectory = Path.Combine(directory.Path, "backup");
        var unrelated = Path.Combine(backupDirectory, "project-20269999-999999999.aeginext");
        await File.WriteAllTextAsync(unrelated, "unrelated");
        var assets = Path.Combine(backupDirectory, "assets");
        Directory.CreateDirectory(assets);
        await File.WriteAllTextAsync(Path.Combine(assets, "keep.png"), "pixels");

        var latest = await ProjectBackupStore.WriteAsync(new(), path, timestamp.AddSeconds(2), 2);

        Assert.False(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.True(File.Exists(latest));
        Assert.True(File.Exists(other));
        Assert.True(File.Exists(unrelated));
        Assert.True(File.Exists(Path.Combine(assets, "keep.png")));
        await ProjectBackupStore.PruneAsync(path, 1);
        Assert.False(File.Exists(second));
        Assert.True(File.Exists(latest));
    }

    [Fact]
    public async Task InvalidOrCancelledBackupCannotDeleteExistingBackups()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        var backup = await ProjectBackupStore.WriteAsync(new(), path, timestamp, 3);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            ProjectBackupStore.WriteAsync(new() { Width = 0 }, path, timestamp.AddSeconds(1), 1));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProjectBackupStore.WriteAsync(new(), path, timestamp.AddSeconds(2), 1, cancellation.Token));

        Assert.True(File.Exists(backup));
        Assert.Single(Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(backup)!));
    }

    [Fact]
    public async Task CancelledPruningKeepsPreviouslyCommittedBackupFiles()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        var first = await ProjectBackupStore.WriteAsync(new(), path, timestamp, 3);
        var second = await ProjectBackupStore.WriteAsync(new(), path, timestamp.AddSeconds(1), 3);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProjectBackupStore.PruneAsync(path, 1, cancellation.Token));

        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
    }

    [Fact]
    public async Task LinkedBackupDirectoryCannotWriteOrPruneOutsideTheProject()
    {
        using var directory = new TemporaryProjectDirectory();
        using var outside = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        var linked = Path.Combine(directory.Path, "backup");
        Directory.CreateSymbolicLink(linked, outside.Path);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ProjectBackupStore.WriteAsync(new(), path, timestamp, 1));
            await Assert.ThrowsAsync<InvalidDataException>(() => ProjectBackupStore.PruneAsync(path, 1));
            Assert.Empty(Directory.EnumerateFileSystemEntries(outside.Path));
        }
        finally
        {
            Directory.Delete(linked);
        }
    }

    [Theory]
    [InlineData("backup/project-20261006-102030123.aeginext", true)]
    [InlineData("backup/project-with-hyphens-20261006-102030123.aeginext", true)]
    [InlineData("backup/project.aeginext", false)]
    [InlineData("backup/project-20269999-102030123.aeginext", false)]
    [InlineData("other/project-20261006-102030123.aeginext", false)]
    [InlineData("backup/project-20261006-102030123.txt", false)]
    public void BackupRecognitionRequiresDirectoryAndStrictTimestamp(string relative, bool expected)
    {
        Assert.Equal(expected, ProjectBackupStore.IsBackupPath(Path.Combine(Path.GetTempPath(), relative)));
    }
}
