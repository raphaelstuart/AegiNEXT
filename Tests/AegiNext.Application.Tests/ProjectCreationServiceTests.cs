using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectCreationServiceTests
{
    [Fact]
    public async Task CreationCommitsNamedProjectAndBackupDirectoryTogether()
    {
        using var directory = new TemporaryProjectDirectory();
        var parent = Path.Combine(directory.Path, "workspace");
        var request = new ProjectCreationRequest("字幕工程 01", parent);

        var created = await ProjectCreationService.CreateAsync(request);

        Assert.Equal(Path.Combine(parent, request.Name, request.Name + ".aeginext"), created.Path);
        Assert.Equal(created.Path, ProjectCreationService.GetProjectPath(request));
        Assert.Equal(request.Name, created.Document.Name);
        Assert.Equal(created.Document.Id, (await ProjectStore.LoadAsync(created.Path)).Id);
        Assert.True(Directory.Exists(Path.Combine(parent, request.Name, "backup")));
        Assert.Single(Directory.EnumerateDirectories(parent));
    }

    [Fact]
    public async Task ExistingEmptyProjectDirectoryIsRejectedWithoutWritingInsideIt()
    {
        using var directory = new TemporaryProjectDirectory();
        var request = new ProjectCreationRequest("Existing", directory.Path);
        var target = Path.Combine(directory.Path, request.Name);
        Directory.CreateDirectory(target);

        await Assert.ThrowsAsync<IOException>(() => ProjectCreationService.CreateAsync(request));

        Assert.Empty(Directory.EnumerateFileSystemEntries(target));
        Assert.Single(Directory.EnumerateDirectories(directory.Path));
    }

    [Fact]
    public async Task ExistingProjectFileIsNeverOverwritten()
    {
        using var directory = new TemporaryProjectDirectory();
        var request = new ProjectCreationRequest("Existing", directory.Path);
        var path = Path.Combine(directory.Path, request.Name, request.Name + ".aeginext");
        await ProjectStore.SaveAsync(new() { Name = "Keep" }, path);
        var bytes = await File.ReadAllBytesAsync(path);

        await Assert.ThrowsAsync<IOException>(() => ProjectCreationService.CreateAsync(request));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.EnumerateDirectories(directory.Path));
    }

    [Fact]
    public async Task ConcurrentCreationHasOneWinnerAndCleansLosingTemporaryDirectory()
    {
        using var directory = new TemporaryProjectDirectory();
        var request = new ProjectCreationRequest("Same project", directory.Path);
        var first = ProjectCreationService.CreateAsync(request);
        var second = ProjectCreationService.CreateAsync(request);
        var attempts = new[] { first, second };

        await Assert.ThrowsAsync<IOException>(() => Task.WhenAll(attempts));

        var winner = await Assert.Single(attempts, task => task.IsCompletedSuccessfully);
        Assert.Equal(winner.Document.Id, (await ProjectStore.LoadAsync(winner.Path)).Id);
        Assert.Single(Directory.EnumerateDirectories(directory.Path));
        Assert.True(Directory.Exists(Path.Combine(directory.Path, request.Name, "backup")));
    }

    [Fact]
    public async Task CancelledCreationLeavesNoProjectOrTemporaryDirectory()
    {
        using var directory = new TemporaryProjectDirectory();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            ProjectCreationService.CreateAsync(new("Cancelled", directory.Path), cancellation.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(directory.Path));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("trailing.")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a:b")]
    [InlineData("a?b")]
    [InlineData("a*b")]
    [InlineData("a|b")]
    [InlineData("a<b")]
    [InlineData("a>b")]
    [InlineData("a\"b")]
    [InlineData("a\nb")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("LPT9")]
    [InlineData("com1.aeginext")]
    public void InvalidPortableNamesAreRejected(string name)
    {
        var error = Assert.Throws<ArgumentException>(() => ProjectCreationService.Validate(new(name, Path.GetTempPath())));
        Assert.Equal("Name", error.ParamName);
    }

    [Fact]
    public void RelativeParentAndOverlongNamesAreRejected()
    {
        Assert.Throws<ArgumentException>(() => ProjectCreationService.Validate(new("Valid", "workspace")));
        Assert.Throws<ArgumentException>(() => ProjectCreationService.Validate(new(new string('a', 129), Path.GetTempPath())));
    }
}
