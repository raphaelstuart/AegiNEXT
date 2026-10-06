using System.Text.Json.Nodes;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Probing;

public sealed class FfprobeToolchainTests
{
    private const string IDENTITY = """
        {"program_version":{"version":"9.0.2"},"library_versions":[
          {"name":"libavutil","major":61,"minor":1,"micro":102,"version":3998054},
          {"name":"libavcodec","major":63,"minor":1,"micro":102,"version":4129126},
          {"name":"libavformat","major":63,"minor":1,"micro":102,"version":4129126},
          {"name":"libswscale","major":10,"minor":1,"micro":102,"version":655718},
          {"name":"libswresample","major":7,"minor":1,"micro":102,"version":459110}]}
        """;

    [Theory]
    [InlineData("9.0.2")]
    [InlineData("9.0.2-full_build-www.gyan.dev")]
    public void AcceptsDeclaredBuildsAndPreservesIdentity(string version)
    {
        var json = JsonNode.Parse(IDENTITY)!;
        json["program_version"]!["version"] = version;

        var identity = FfprobeToolchain.ReadIdentity(json.ToJsonString(), "explicit/path", "hash");

        Assert.Equal(version, identity.Version);
        Assert.Equal("explicit/path", identity.ExecutablePath);
        Assert.Equal("hash", identity.Sha256);
        Assert.Equal("63.1.102", identity.LibraryVersions["libavformat"]);
        Assert.Equal("10.1.102", identity.LibraryVersions["libswscale"]);
        Assert.Equal("7.1.102", identity.LibraryVersions["libswresample"]);
    }

    [Theory]
    [InlineData("9.0.2-dev")]
    [InlineData("9.0.3")]
    [InlineData("8.0.2")]
    [InlineData("N-unknown")]
    public void RejectsVersionsOutsideTheManifest(string version)
    {
        var json = JsonNode.Parse(IDENTITY)!;
        json["program_version"]!["version"] = version;
        Assert.Throws<InvalidDataException>(() => FfprobeToolchain.ReadIdentity(json.ToJsonString(), "path", "hash"));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"program_version\":{\"version\":\"9.0.2\"},\"library_versions\":[]}")]
    public void RejectsIncompleteIdentity(string json)
    {
        Assert.Throws<InvalidDataException>(() => FfprobeToolchain.ReadIdentity(json, "path", "hash"));
    }

    [Fact]
    public void RejectsMixedLibraryVersions()
    {
        var json = JsonNode.Parse(IDENTITY)!;
        json["library_versions"]![1]!["major"] = 62;
        Assert.Throws<InvalidDataException>(() => FfprobeToolchain.ReadIdentity(json.ToJsonString(), "path", "hash"));
    }

    [Theory]
    [InlineData(4129127)]
    [InlineData(null)]
    public void RequiresMatchingRuntimeLibraryVersions(int? runtimeVersion)
    {
        var json = JsonNode.Parse(IDENTITY)!;
        json["library_versions"]![1]!["version"] = runtimeVersion;
        Assert.Throws<InvalidDataException>(() => FfprobeToolchain.ReadIdentity(json.ToJsonString(), "path", "hash"));
    }

    [Fact]
    public void OptionsRequireAnExplicitPathAndFinitePositiveLimits()
    {
        var fullPath = Path.Combine(Path.GetTempPath(), "ffprobe");
        Assert.Throws<ArgumentException>(() => new FfprobeOptions("ffprobe"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FfprobeOptions(fullPath, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FfprobeOptions(fullPath, TimeSpan.FromHours(1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FfprobeOptions(fullPath, maximumOutputCharacters: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FfprobeOptions(fullPath, maximumErrorCharacters: 0));
    }
}
