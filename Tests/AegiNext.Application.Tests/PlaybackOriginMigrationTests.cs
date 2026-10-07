using System.Text;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class PlaybackOriginMigrationTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void LegacyProjectsGainUnknownPlaybackOriginWithoutShiftingExistingTimes(int version)
    {
        var original = Document();
        var json = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        json["version"] = version;
        json["media"]!.AsObject().Remove("playbackOrigin");

        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(ProjectDocument.CURRENT_VERSION, restored.Version);
        Assert.Null(restored.Media!.PlaybackOrigin);
        Assert.Equal(original.Media!.MediaOrigin, restored.Media.MediaOrigin);
        Assert.Equal(ProjectStore.Serialize(original), ProjectStore.Serialize(restored));
    }

    [Fact]
    public void CurrentProjectRequiresExplicitNullablePlaybackOriginAndPreservesConfirmedRationalOrigin()
    {
        var original = Document();
        original = original with { Media = original.Media! with { PlaybackOrigin = new(-1001, 30000) } };
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(original));
        Assert.Equal(original.Media, restored.Media);

        var json = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        json["media"]!.AsObject().Remove("playbackOrigin");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    /// <summary>播放零点在版本 6 引入，后续版本升级不得再次执行旧字段迁移。</summary>
    [Fact]
    public void VersionSixPreservesConfirmedRationalPlaybackOriginDuringIdentityUpgrade()
    {
        var original = Document();
        original = original with { Media = original.Media! with { PlaybackOrigin = new(-1001, 30000) } };
        var json = JsonNode.Parse(ProjectStore.Serialize(original))!.AsObject();
        json["version"] = 6;
        json["subtitles"]![0]!.AsObject().Remove("stylePresetId");

        var restored = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString()));

        Assert.Equal(7, restored.Version);
        Assert.Equal(original.Media, restored.Media);
        Assert.Equal(original.Subtitles[0].Start, restored.Subtitles[0].Start);
        Assert.Equal(original.Subtitles[0].End, restored.Subtitles[0].End);
    }

    /// <summary>版本 6 已要求显式播放零点字段，缺失不得被升级默认为未知。</summary>
    [Fact]
    public void VersionSixStillRejectsMissingRequiredPlaybackOrigin()
    {
        var json = JsonNode.Parse(ProjectStore.Serialize(Document()))!.AsObject();
        json["version"] = 6;
        json["media"]!.AsObject().Remove("playbackOrigin");

        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    private static ProjectDocument Document()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: "/media/source.mkv");
        var line = new SubtitleLine { Start = new(-1001, 30000), End = new(2002, 30000), Text = "cue" };
        return new()
        {
            Assets = [asset], Media = new(asset.Id, 0, 1, new(1001, 30000)), Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
    }
}
