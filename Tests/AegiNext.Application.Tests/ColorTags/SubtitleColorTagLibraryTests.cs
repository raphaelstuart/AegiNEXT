using System.Collections.Immutable;
using System.Text;
using AegiNext.Application.ColorTags;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests.ColorTags;

public sealed class SubtitleColorTagLibraryTests
{
    [Fact]
    public async Task MissingLibraryInitializesDefaultsOnceAndExistingEmptyLibraryStaysEmpty()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "tags.json");
        var tag = new SubtitleColorTag { Name = "Review", ColorHex = "#112233" };
        using (var library = new SubtitleColorTagLibrary(path, [tag]))
        {
            await library.LoadAsync();
            Assert.Equal(tag, Assert.Single(library.Snapshot.Tags));
            Assert.True(File.Exists(path));
            await library.ReplaceAsync(new());
        }
        using var reopened = new SubtitleColorTagLibrary(path, [tag]);
        await reopened.LoadAsync();
        Assert.Empty(reopened.Snapshot.Tags);
    }

    [Fact]
    public async Task CancelledCommitAndInvalidReplacementPreserveDiskAndSnapshot()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "tags.json");
        using var library = new SubtitleColorTagLibrary(path);
        await library.LoadAsync();
        var before = library.Snapshot;
        var bytes = await File.ReadAllBytesAsync(path);
        var next = new SubtitleColorTagLibraryDocument { Tags = [new() { Name = "Review", ColorHex = "#112233" }] };
        await Assert.ThrowsAsync<OperationCanceledException>(() => library.ReplaceAsync(next, () => throw new OperationCanceledException()));
        Assert.Same(before, library.Snapshot);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        await Assert.ThrowsAsync<InvalidDataException>(() => library.ReplaceAsync(next with { Version = 2 }));
        Assert.Same(before, library.Snapshot);
    }

    [Theory]
    [InlineData("{\"version\":1,\"tags\":[],\"unknown\":1}")]
    [InlineData("{\"version\":1,\"version\":1,\"tags\":[]}")]
    [InlineData("{\"version\":1}")]
    [InlineData("{\"version\":1,\"tags\":null}")]
    [InlineData("{\"version\":2,\"tags\":[]}")]
    [InlineData("{\"version\":1,\"tags\":[{\"id\":\"d1aee330-cbae-441e-b734-af69c78c2455\",\"name\":\"Review\"}]}")]
    public void InvalidStoreDataIsStrictlyRejected(string json)
    {
        Assert.Throws<InvalidDataException>(() => SubtitleColorTagStore.Deserialize(Encoding.UTF8.GetBytes(json)));
    }

    [Fact]
    public async Task ConcurrentUpsertsPublishEveryCommittedTagAndCorruptLoadPreservesSnapshot()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "tags.json");
        using var library = new SubtitleColorTagLibrary(path);
        var tags = Enumerable.Range(0, 8).Select(index => new SubtitleColorTag { Name = "Tag " + index, ColorHex = "#112233" }).ToArray();
        await Task.WhenAll(tags.Select(tag => library.UpsertAsync(tag)));
        Assert.Equal(tags.Length, library.Snapshot.Tags.Length);
        var before = library.Snapshot;
        await File.WriteAllTextAsync(path, "invalid");
        await Assert.ThrowsAsync<InvalidDataException>(() => library.LoadAsync());
        Assert.Same(before, library.Snapshot);
    }

    [Fact]
    public async Task LateBoundDefaultsAreUsedOnlyForTheFirstMissingFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, "tags.json");
        using var library = new SubtitleColorTagLibrary(path);
        var tag = new SubtitleColorTag { Name = "审核", ColorHex = "#112233" };
        await library.LoadAsync([tag]);
        Assert.Equal(tag, Assert.Single(library.Snapshot.Tags));
        await library.LoadAsync([tag with { Name = "Review" }]);
        Assert.Equal(tag, Assert.Single(library.Snapshot.Tags));
    }

    [Fact]
    public async Task ReplacePublishesOnlyAfterCommitAndIdenticalContentKeepsSnapshot()
    {
        using var directory = new TemporaryProjectDirectory();
        using var library = new SubtitleColorTagLibrary(Path.Combine(directory.Path, "tags.json"));
        await library.LoadAsync();
        var before = library.Snapshot;
        var next = new SubtitleColorTagLibraryDocument { Tags = [new() { Name = "Review", ColorHex = "#112233" }] };
        await library.ReplaceAsync(next, () => Assert.Same(before, library.Snapshot));
        Assert.Same(next, library.Snapshot);
        await library.ReplaceAsync(next with { Tags = next.Tags.ToArray().ToImmutableArray() });
        Assert.Same(next, library.Snapshot);
    }
}
