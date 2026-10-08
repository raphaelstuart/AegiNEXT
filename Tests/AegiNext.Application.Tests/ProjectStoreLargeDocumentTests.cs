using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectStoreLargeDocumentTests
{
    [Fact]
    public async Task ProjectLargerThanFormerFileBudgetCanBeSavedAndLoadedWithoutChangingContent()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "large.aeginext");
        var document = CreateLargeDocument();

        await ProjectStore.SaveAsync(document, path);

        Assert.True(new FileInfo(path).Length > 32 * 1024 * 1024);
        var loaded = await ProjectStore.LoadAsync(path);
        Assert.Equal(document.Id, loaded.Id);
        Assert.Equal(document.Subtitles.Length, loaded.Subtitles.Length);
        Assert.Equal(document.Subtitles.Select(line => (line.Id, line.Start, line.End, line.Text)),
            loaded.Subtitles.Select(line => (line.Id, line.Start, line.End, line.Text)));
        Assert.Single(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void ProjectLargerThanFormerFileBudgetCanRoundTripThroughMemorySerialization()
    {
        var document = CreateLargeDocument();

        var bytes = ProjectStore.Serialize(document);
        var loaded = ProjectStore.Deserialize(bytes);

        Assert.True(bytes.Length > 32 * 1024 * 1024);
        Assert.Equal(document.Subtitles.Select(line => (line.Id, line.Text)),
            loaded.Subtitles.Select(line => (line.Id, line.Text)));
    }

    [Fact]
    public async Task CancellationDuringLargeSaveKeepsExistingProjectAndRemovesTemporaryFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        await ProjectStore.SaveAsync(new(), path);
        var original = await File.ReadAllBytesAsync(path);
        using var cancellation = new CancellationTokenSource();

        var pending = ProjectStore.SaveAsync(CreateLargeDocument(), path, cancellation.Token);
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task FailedAtomicCommitKeepsDestinationDirectoryAndRemovesTemporaryFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "project.aeginext");
        Directory.CreateDirectory(path);
        var marker = Path.Combine(path, "marker.txt");
        await File.WriteAllTextAsync(marker, "keep existing destination");

        await Assert.ThrowsAsync<IOException>(() => ProjectStore.SaveAsync(new(), path));

        Assert.Equal("keep existing destination", await File.ReadAllTextAsync(marker));
        Assert.Single(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public async Task CancellationDuringLargeLoadDoesNotReturnADocumentOrChangeTheFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "large.aeginext");
        await ProjectStore.SaveAsync(CreateLargeDocument(), path);
        var length = new FileInfo(path).Length;
        using var cancellation = new CancellationTokenSource();

        var pending = ProjectStore.LoadAsync(path, cancellation.Token);
        Assert.False(pending.IsCompleted);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(length, new FileInfo(path).Length);
        Assert.Single(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void WarmStreamedFingerprintDoesNotAllocateACompleteLargeJsonByteArray()
    {
        var document = CreateLargeDocument();
        var expected = ProjectStore.ComputeFingerprint(document);

        var before = GC.GetAllocatedBytesForCurrentThread();
        var actual = ProjectStore.ComputeFingerprint(document);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Equal(expected, actual);
        Assert.True(allocated < 4 * 1024 * 1024, $"大工程指纹分配了 {allocated:N0} 字节。");
    }

    private static ProjectDocument CreateLargeDocument()
    {
        var text = new string('a', 256 * 1024);
        var subtitles = Enumerable.Range(0, 129).Select(index => new SubtitleLine
        {
            Start = new(index * 2),
            End = new(index * 2 + 1),
            Text = text
        }).ToImmutableArray();
        return new()
        {
            Subtitles = subtitles,
            Layers = subtitles.Select(line => new ProjectLayer
            {
                Id = line.Id,
                Kind = LayerKind.SUBTITLE,
                SubtitleId = line.Id,
                Start = line.Start,
                End = line.End
            }).ToImmutableArray()
        };
    }
}
