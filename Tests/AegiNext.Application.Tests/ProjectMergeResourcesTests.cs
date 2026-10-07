using System.Collections.Immutable;
using System.Security.Cryptography;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectMergeResourcesTests
{
    [Fact]
    public async Task PreparationCopiesOnlyReferencedFontsAndImagesAndLeavesDestinationUntouched()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var image = await CreateAssetAsync(sourceDirectory.Path, "image.png", ProjectAssetKind.IMAGE, "image bytes");
        var font = await CreateAssetAsync(sourceDirectory.Path, "font.ttf", ProjectAssetKind.FONT, "font bytes");
        var missing = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "missing.png");
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "missing.mkv");
        var document = ImageProject(image) with
        {
            Assets = [image, font, missing, media],
            Media = new(media.Id, 0, null, new(0)),
            SubtitleTracks = [new()
            {
                DefaultStyle = new() { FontAssetId = font.Id },
                StylePresetId = Guid.NewGuid(),
                StylePresetName = "Member style"
            }]
        };
        var source = new ProjectMergeSource(document, "Member", sourceDirectory.Path);

        await using var prepared = await ProjectMergeResources.PrepareAsync([source], destination.Path);

        var result = Assert.Single(prepared.Sources).Document;
        ProjectValidator.Validate(result);
        Assert.Same(document, source.Document);
        Assert.Same(missing, result.Assets[2]);
        Assert.Same(media, result.Assets[3]);
        Assert.Equal(document.Media, result.Media);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        Assert.Equal(image.Id, result.Assets[0].Id);
        Assert.Equal(font.Id, result.Assets[1].Id);
        Assert.StartsWith("assets/", result.Assets[0].RelativePath, StringComparison.Ordinal);
        Assert.StartsWith("assets/", result.Assets[1].RelativePath, StringComparison.Ordinal);

        await prepared.CommitAsync();
        prepared.Accept();

        Assert.Equal("image bytes", await File.ReadAllTextAsync(ProjectAssetLocation.Resolve(result.Assets[0], destination.Path)));
        Assert.Equal("font bytes", await File.ReadAllTextAsync(ProjectAssetLocation.Resolve(result.Assets[1], destination.Path)));
        Assert.Equal(2, Directory.EnumerateFiles(destination.Path, "*", SearchOption.AllDirectories).Count());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailureInLaterSourceLeavesNoPublishedAssets(bool badHash)
    {
        using var firstDirectory = new TemporaryProjectDirectory();
        using var secondDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var first = await CreateAssetAsync(firstDirectory.Path, "first.png", ProjectAssetKind.IMAGE, $"first-{Guid.NewGuid():N}");
        var second = badHash
            ? (await CreateAssetAsync(secondDirectory.Path, "second.png", ProjectAssetKind.IMAGE, "second")) with { Sha256 = new string('a', 64) }
            : new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "missing.png");
        ProjectMergeSource[] sources =
        [
            new(ImageProject(first), "First", firstDirectory.Path),
            new(ImageProject(second), "Second", secondDirectory.Path)
        ];

        if (badHash)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => ProjectMergeResources.PrepareAsync(sources, destination.Path));
        }
        else
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => ProjectMergeResources.PrepareAsync(sources, destination.Path));
        }

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
        Assert.DoesNotContain(Directory.GetDirectories(Path.GetTempPath(), "aeginext-merge-*"),
            directory => File.Exists(Path.Combine(directory, "assets", first.Sha256 + ".png")));
    }

    [Fact]
    public async Task DisposingUnacceptedCommitRemovesOnlyNewFiles()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var existingSource = await CreateAssetAsync(sourceDirectory.Path, "existing.png", ProjectAssetKind.IMAGE, "existing");
        var newSource = await CreateAssetAsync(sourceDirectory.Path, "new.png", ProjectAssetKind.IMAGE, "new");
        var existing = await ProjectResources.ImportAsync(ProjectAssetLocation.Resolve(existingSource, sourceDirectory.Path),
            ProjectAssetKind.IMAGE, destination.Path);
        var existingPath = ProjectAssetLocation.Resolve(existing, destination.Path);
        var document = ImageProject(existingSource, newSource);

        await using (var prepared = await ProjectMergeResources.PrepareAsync(
                         [new(document, "Member", sourceDirectory.Path)], destination.Path))
        {
            await prepared.CommitAsync();
            Assert.Equal(2, Directory.EnumerateFiles(destination.Path, "*", SearchOption.AllDirectories).Count());
        }

        Assert.Equal("existing", await File.ReadAllTextAsync(existingPath));
        Assert.Single(Directory.EnumerateFiles(destination.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task AcceptedFilesRemainAvailableAfterUndoAndRedo()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var content = $"image-{Guid.NewGuid():N}";
        var image = await CreateAssetAsync(sourceDirectory.Path, "image.png", ProjectAssetKind.IMAGE, content);
        var editor = new ProjectEditor();
        ProjectDocument merged;
        string stagingDirectory;
        await using (var prepared = await ProjectMergeResources.PrepareAsync(
                         [new(ImageProject(image), "Member", sourceDirectory.Path)], destination.Path))
        {
            stagingDirectory = Assert.Single(Directory.GetDirectories(Path.GetTempPath(), "aeginext-merge-*"),
                directory => File.Exists(ProjectAssetLocation.Resolve(prepared.Sources[0].Document.Assets[0], directory)));
            merged = ProjectEditingOperations.MergeProjects(editor.Snapshot, prepared.Sources).Document;
            await prepared.CommitAsync();
            editor.Apply("Merge projects", _ => merged);
            prepared.Accept();
        }

        Assert.False(Directory.Exists(stagingDirectory));

        editor.Undo();
        Assert.Empty(editor.Snapshot.Layers);
        var path = ProjectAssetLocation.Resolve(Assert.Single(merged.Assets), destination.Path);
        Assert.Equal(content, await File.ReadAllTextAsync(path));
        editor.Redo();
        Assert.Same(merged, editor.Snapshot);
        Assert.Equal(content, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task IdenticalSourceAssetsKeepSourceIdsAndShareOnePublishedFile()
    {
        using var firstDirectory = new TemporaryProjectDirectory();
        using var secondDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var first = await CreateAssetAsync(firstDirectory.Path, "first.png", ProjectAssetKind.IMAGE, "shared bytes");
        var second = await CreateAssetAsync(secondDirectory.Path, "second.png", ProjectAssetKind.IMAGE, "shared bytes");
        await using var prepared = await ProjectMergeResources.PrepareAsync(
            [new(ImageProject(first), "First", firstDirectory.Path), new(ImageProject(second), "Second", secondDirectory.Path)],
            destination.Path);

        Assert.Equal(first.Id, Assert.Single(prepared.Sources[0].Document.Assets).Id);
        Assert.Equal(second.Id, Assert.Single(prepared.Sources[1].Document.Assets).Id);
        Assert.Equal(prepared.Sources[0].Document.Assets[0].RelativePath, prepared.Sources[1].Document.Assets[0].RelativePath);
        await prepared.CommitAsync();
        prepared.Accept();

        Assert.Single(Directory.EnumerateFiles(destination.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task ExistingCanonicalFileChangedBeforeCommitRejectsBatchAndRollsBackNewFiles()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var first = await CreateAssetAsync(sourceDirectory.Path, "first.png", ProjectAssetKind.IMAGE, "first");
        var second = await CreateAssetAsync(sourceDirectory.Path, "second.png", ProjectAssetKind.IMAGE, "second");
        var existing = await ProjectResources.ImportAsync(ProjectAssetLocation.Resolve(second, sourceDirectory.Path),
            ProjectAssetKind.IMAGE, destination.Path);
        var existingPath = ProjectAssetLocation.Resolve(existing, destination.Path);
        await using var prepared = await ProjectMergeResources.PrepareAsync(
            [new(ImageProject(first, second), "Member", sourceDirectory.Path)], destination.Path);
        await File.WriteAllTextAsync(existingPath, "externally changed");

        await Assert.ThrowsAsync<InvalidDataException>(() => prepared.CommitAsync());

        Assert.Equal("externally changed", await File.ReadAllTextAsync(existingPath));
        Assert.Single(Directory.EnumerateFiles(destination.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CancelledCommitAndDisposalPublishNothing()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var image = await CreateAssetAsync(sourceDirectory.Path, "image.png", ProjectAssetKind.IMAGE, "image bytes");
        await using var prepared = await ProjectMergeResources.PrepareAsync(
            [new(ImageProject(image), "Member", sourceDirectory.Path)], destination.Path);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => prepared.CommitAsync(cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }

    [Fact]
    public async Task CancelledPreparationDoesNotTouchDestination()
    {
        using var destination = new TemporaryProjectDirectory();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProjectMergeResources.PrepareAsync([], destination.Path, cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }

    [Fact]
    public async Task SourceSymbolicLinkIsRejectedWithoutPublishingAssets()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var image = await CreateAssetAsync(sourceDirectory.Path, "original.png", ProjectAssetKind.IMAGE, "image bytes");
        File.CreateSymbolicLink(Path.Combine(sourceDirectory.Path, "linked.png"), Path.Combine(sourceDirectory.Path, "original.png"));
        var linked = image with { RelativePath = "linked.png" };

        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectMergeResources.PrepareAsync(
            [new(ImageProject(linked), "Member", sourceDirectory.Path)], destination.Path));

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }

    [Fact]
    public async Task DestinationAssetDirectorySymbolicLinkIsRejectedWithoutChangingLinkedDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        using var external = new TemporaryProjectDirectory();
        var image = await CreateAssetAsync(sourceDirectory.Path, "image.png", ProjectAssetKind.IMAGE, "image bytes");
        Directory.CreateSymbolicLink(Path.Combine(destination.Path, "assets"), external.Path);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await using var prepared = await ProjectMergeResources.PrepareAsync(
                [new(ImageProject(image), "Member", sourceDirectory.Path)], destination.Path);
            await prepared.CommitAsync();
        });

        Assert.Empty(Directory.EnumerateFileSystemEntries(external.Path));
    }

    [Fact]
    public async Task AcceptBeforeCommitIsRejectedAndDisposalIsIdempotent()
    {
        using var destination = new TemporaryProjectDirectory();
        var prepared = await ProjectMergeResources.PrepareAsync([], destination.Path);

        Assert.Throws<InvalidOperationException>(() => prepared.Accept());
        await prepared.DisposeAsync();
        await prepared.DisposeAsync();

        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }

    [Fact]
    public async Task MissingScratchDirectoryIsCreatedOnlyWhenPublishing()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var parentDirectory = new TemporaryProjectDirectory();
        var destination = Path.Combine(parentDirectory.Path, "unsaved-project");
        var image = await CreateAssetAsync(sourceDirectory.Path, "image.png", ProjectAssetKind.IMAGE, "image bytes");
        await using var prepared = await ProjectMergeResources.PrepareAsync(
            [new(ImageProject(image), "Member", sourceDirectory.Path)], destination);

        Assert.False(Directory.Exists(destination));
        await prepared.CommitAsync();
        prepared.Accept();

        Assert.Equal("image bytes", await File.ReadAllTextAsync(
            ProjectAssetLocation.Resolve(Assert.Single(prepared.Sources[0].Document.Assets), destination)));
    }

    [Fact]
    public async Task MissingScratchDirectoryStaysAbsentWhenPreparationFails()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var parentDirectory = new TemporaryProjectDirectory();
        var destination = Path.Combine(parentDirectory.Path, "unsaved-project");
        var missing = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.IMAGE, "missing.png");

        await Assert.ThrowsAsync<FileNotFoundException>(() => ProjectMergeResources.PrepareAsync(
            [new(ImageProject(missing), "Member", sourceDirectory.Path)], destination));

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task DisposingUnacceptedCommitRemovesNewScratchDirectory()
    {
        using var sourceDirectory = new TemporaryProjectDirectory();
        using var parentDirectory = new TemporaryProjectDirectory();
        var destination = Path.Combine(parentDirectory.Path, "unsaved-project");
        var image = await CreateAssetAsync(sourceDirectory.Path, "image.png", ProjectAssetKind.IMAGE, "image bytes");
        await using (var prepared = await ProjectMergeResources.PrepareAsync(
                         [new(ImageProject(image), "Member", sourceDirectory.Path)], destination))
        {
            await prepared.CommitAsync();
            Assert.True(Directory.Exists(destination));
        }

        Assert.False(Directory.Exists(destination));
    }

    [Fact]
    public async Task DestinationCanonicalFileSymbolicLinkDoesNotModifyLinkedFile()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var sourceDirectory = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        using var external = new TemporaryProjectDirectory();
        var image = await CreateAssetAsync(sourceDirectory.Path, "image.png", ProjectAssetKind.IMAGE, "image bytes");
        await using var prepared = await ProjectMergeResources.PrepareAsync(
            [new(ImageProject(image), "Member", sourceDirectory.Path)], destination.Path);
        var linkedPath = Path.Combine(external.Path, "keep.png");
        await File.WriteAllTextAsync(linkedPath, "keep linked bytes");
        var canonical = ProjectAssetLocation.Resolve(prepared.Sources[0].Document.Assets[0], destination.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
        File.CreateSymbolicLink(canonical, linkedPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => prepared.CommitAsync());

        Assert.Equal("keep linked bytes", await File.ReadAllTextAsync(linkedPath));
        Assert.NotNull(new FileInfo(canonical).LinkTarget);
    }

    private static ProjectDocument ImageProject(params ProjectAsset[] images)
    {
        return new()
        {
            Assets = images.ToImmutableArray(),
            Layers = images.Select(image => new ProjectLayer
            {
                Kind = LayerKind.IMAGE,
                Image = new(image.Id, 1, 1)
            }).ToImmutableArray()
        };
    }

    private static async Task<ProjectAsset> CreateAssetAsync(string directory, string fileName, ProjectAssetKind kind, string content)
    {
        var path = Path.Combine(directory, fileName);
        await File.WriteAllTextAsync(path, content);
        var hash = Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        return new(Guid.NewGuid(), kind, fileName, hash);
    }
}
