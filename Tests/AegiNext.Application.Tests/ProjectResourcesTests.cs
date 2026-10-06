using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectResourcesTests
{
    [Fact]
    public void NormalizingMediaLeavesMissingManagedResourcesAndTheirHashesUntouched()
    {
        using var directory = new TemporaryProjectDirectory();
        var media = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(directory.Path, "video.mkv"));
        var font = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.FONT, "assets/missing.ttf", new string('a', 64));
        var document = new ProjectDocument { Assets = [media, font] };

        var normalized = ProjectResources.NormalizeMediaReferences(document, directory.Path);

        Assert.Equal(media with { RelativePath = "video.mkv", ExternalPath = null }, normalized.Assets[0]);
        Assert.Same(font, normalized.Assets[1]);
        Assert.Same(normalized, ProjectResources.NormalizeMediaReferences(normalized, directory.Path));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Fact]
    public void NormalizingForeignExternalReferencesPreservesSnapshotIdentity()
    {
        using var directory = new TemporaryProjectDirectory();
        var foreign = OperatingSystem.IsWindows() ? "/Volumes/Media/video.mkv" : "C:\\Media\\video.mkv";
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: foreign);
        var document = new ProjectDocument { Assets = [asset] };

        Assert.Same(document, ProjectResources.NormalizeMediaReferences(document, directory.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MediaInsideProjectUsesCanonicalRelativeReferenceWithoutCopy(bool absoluteInput)
    {
        using var directory = new TemporaryProjectDirectory();
        var mediaDirectory = Path.Combine(directory.Path, "media");
        Directory.CreateDirectory(mediaDirectory);
        var path = Path.Combine(mediaDirectory, "字幕 01.mkv");
        await File.WriteAllTextAsync(path, "video");

        var asset = await ProjectResources.ImportAsync(absoluteInput ? path : "./media/../media/字幕 01.mkv",
            ProjectAssetKind.MEDIA, directory.Path);

        Assert.Equal("media/字幕 01.mkv", asset.RelativePath);
        Assert.Null(asset.ExternalPath);
        Assert.Null(asset.Sha256);
        Assert.Equal(path, ProjectAssetLocation.Resolve(asset, directory.Path));
        Assert.Single(Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories));
        ProjectValidator.Validate(new() { Assets = [asset] });
    }

    [Fact]
    public async Task MediaOutsideProjectWithCommonDirectoryPrefixRemainsExternal()
    {
        using var directory = new TemporaryProjectDirectory();
        var projectDirectory = Path.Combine(directory.Path, "project");
        var mediaDirectory = Path.Combine(directory.Path, "project-other");
        Directory.CreateDirectory(projectDirectory);
        Directory.CreateDirectory(mediaDirectory);
        var path = Path.Combine(mediaDirectory, "video.mkv");
        await File.WriteAllTextAsync(path, "video");

        var asset = await ProjectResources.ImportAsync("../project-other/video.mkv", ProjectAssetKind.MEDIA,
            projectDirectory);

        Assert.Equal(string.Empty, asset.RelativePath);
        Assert.Equal(path, asset.ExternalPath);
        Assert.Empty(Directory.EnumerateFileSystemEntries(projectDirectory));
    }

    [Fact]
    public async Task FirstSaveReclassifiesExternalMediaWithoutRequiringTheFileToExist()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var source = Path.Combine(destination.Path, "media", "video.mkv");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            new string('a', 64), source);
        var document = new ProjectDocument { Assets = [asset], Media = new(asset.Id, 0, null, new(0)) };

        var rebased = await ProjectResources.RebaseAsync(document, original.Path, destination.Path);

        Assert.Equal(asset with { RelativePath = "media/video.mkv", ExternalPath = null },
            Assert.Single(rebased.Assets));
        Assert.Equal(document.Media, rebased.Media);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));

        var restored = await ProjectResources.RebaseAsync(rebased, destination.Path, original.Path);

        Assert.Equal(asset, Assert.Single(restored.Assets));
        Assert.Equal(document.Media, restored.Media);
        Assert.Empty(Directory.EnumerateFileSystemEntries(original.Path));
    }

    [Fact]
    public async Task RebaseKeepsRelativeMediaWhenTheSourceIsInsideTheNewDirectory()
    {
        using var directory = new TemporaryProjectDirectory();
        var original = Path.Combine(directory.Path, "original");
        Directory.CreateDirectory(original);
        var path = Path.Combine(original, "video.mkv");
        await File.WriteAllTextAsync(path, "video");
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, "video.mkv");

        var rebased = await ProjectResources.RebaseAsync(new() { Assets = [asset] }, original, directory.Path);

        Assert.Equal(asset with { RelativePath = "original/video.mkv" }, Assert.Single(rebased.Assets));
        Assert.Equal(path, ProjectAssetLocation.Resolve(rebased.Assets[0], directory.Path));
        Assert.Single(Directory.EnumerateFiles(directory.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task RebasePreservesForeignAndMissingExternalReferences()
    {
        using var original = new TemporaryProjectDirectory();
        using var destination = new TemporaryProjectDirectory();
        var foreign = OperatingSystem.IsWindows() ? "/Volumes/Media/video.mkv" : "C:\\Media\\video.mkv";
        var assets = new[]
        {
            new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: foreign),
            new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
                ExternalPath: Path.Combine(original.Path, "missing.mkv"))
        };

        var rebased = await ProjectResources.RebaseAsync(new() { Assets = [.. assets] }, original.Path,
            destination.Path);

        Assert.Equal(assets, rebased.Assets);
        Assert.Empty(Directory.EnumerateFileSystemEntries(destination.Path));
    }

    [Theory]
    [InlineData("video:01.mkv")]
    [InlineData("video\\01.mkv")]
    public async Task NativeFileNamesThatCannotBePortableRemainExternal(string name)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var directory = new TemporaryProjectDirectory();
        var path = Path.Combine(directory.Path, name);
        await File.WriteAllTextAsync(path, "video");

        var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.MEDIA, directory.Path);

        Assert.Equal(string.Empty, asset.RelativePath);
        Assert.Equal(path, asset.ExternalPath);
        ProjectValidator.Validate(new() { Assets = [asset] });
    }
}
