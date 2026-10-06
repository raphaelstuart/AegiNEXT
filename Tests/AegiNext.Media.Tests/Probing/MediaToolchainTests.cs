using AegiNext.Media.Probing;
using Xunit;

namespace AegiNext.Media.Tests.Probing;

public sealed class MediaToolchainTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "aeginext-tools-" + Guid.NewGuid().ToString("N"));

    public MediaToolchainTests()
    {
        Directory.CreateDirectory(directory);
    }

    [Theory]
    [InlineData(false, "ffprobe")]
    [InlineData(true, "ffprobe.exe")]
    public void PackagedProbeUsesItsOwnToolAndIgnoresExternalOverrides(bool windows, string filename)
    {
        File.WriteAllText(Path.Combine(directory, "media-runtime.json"), "{}");
        var expected = CreateTool("tools", filename);
        var external = CreateTool("external", filename);
        Assert.Equal(expected, MediaToolchain.Resolve("ffprobe", external, directory, external, Path.GetDirectoryName(external), windows));
    }

    [Fact]
    public void BrokenPackageDoesNotFallBackToDevelopmentPath()
    {
        File.WriteAllText(Path.Combine(directory, "media-runtime.json"), "{}");
        var external = CreateTool("external", "ffmpeg");
        Assert.Throws<FileNotFoundException>(() => MediaToolchain.Resolve("ffmpeg", external, directory, external, Path.GetDirectoryName(external), false));
    }

    [Fact]
    public void DeveloperExplicitPathWinsAndMissingExplicitPathFails()
    {
        var expected = CreateTool("external", "ffprobe");
        CreateTool("tools", "ffprobe");
        Assert.Equal(expected, MediaToolchain.Resolve("ffprobe", expected, directory, null, null, false));
        Assert.Throws<FileNotFoundException>(() => MediaToolchain.Resolve("ffprobe", expected + ".missing", directory, null, null, false));
    }

    [Fact]
    public void AdjacentToolsWinOverPathAndSupportNonAsciiSpaces()
    {
        var expected = CreateTool("tools", "ffmpeg");
        var external = CreateTool("中文 路径", "ffmpeg");
        Assert.Equal(expected, MediaToolchain.Resolve("ffmpeg", null, directory, null, Path.GetDirectoryName(external), false));
        File.Delete(expected);
        Assert.Equal(external, MediaToolchain.Resolve("ffmpeg", null, directory, null, Path.GetDirectoryName(external), false));
    }

    private string CreateTool(string folder, string filename)
    {
        var target = Path.Combine(directory, folder);
        Directory.CreateDirectory(target);
        var path = Path.Combine(target, filename);
        File.WriteAllText(path, string.Empty);
        return path;
    }

    public void Dispose()
    {
        Directory.Delete(directory, true);
        GC.SuppressFinalize(this);
    }
}
