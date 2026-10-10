using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class VersionElevenProjectMigrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VersionElevenGroupsAndExactStylesSurviveEveryEntryAndCurrentVersionRoundTrip(bool includeRangeStyles)
    {
        var root = VersionElevenProjectJsonFixture.Create(includeRangeStyles);
        Assert.Equal(11, root["version"]!.GetValue<int>());
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        using var parsed = JsonDocument.Parse(bytes);
        using var directory = new TemporaryProjectDirectory();

        var fromBytes = ProjectStore.Deserialize(bytes);
        AssertPreserved(fromBytes, includeRangeStyles);
        var fromElement = ProjectStore.Deserialize(parsed.RootElement);
        AssertPreserved(fromElement, includeRangeStyles);
        Assert.Equal(ProjectStore.Serialize(fromBytes), ProjectStore.Serialize(fromElement));

        var path = Path.Combine(directory.Path, "version-eleven.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        var fromFile = await ProjectStore.LoadAsync(path);
        AssertPreserved(fromFile, includeRangeStyles);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(ProjectStore.Serialize(fromBytes), ProjectStore.Serialize(fromFile));

        var currentBytes = ProjectStore.Serialize(fromFile);
        using var currentJson = JsonDocument.Parse(currentBytes);
        Assert.Equal(ProjectDocument.CURRENT_VERSION, currentJson.RootElement.GetProperty("version").GetInt32());
        var reopened = ProjectStore.Deserialize(currentBytes);
        AssertPreserved(reopened, includeRangeStyles);
        Assert.Equal(currentBytes, ProjectStore.Serialize(reopened));
    }

    [Theory]
    [InlineData("activeStyle")]
    [InlineData("inactiveStyle")]
    [InlineData("animationRanges")]
    [InlineData("textRangeId")]
    [InlineData("state")]
    [InlineData("colorSpace")]
    [InlineData("componentMask")]
    [InlineData("mode")]
    [InlineData("FONT_SIZE")]
    [InlineData("SHADOW_OFFSET")]
    [InlineData("unknown")]
    public async Task VersionElevenRejectsOldClipFieldsAndUndeclaredAnimationFieldsAtEveryEntry(string field)
    {
        var root = VersionElevenProjectJsonFixture.Create(false);
        var subtitle = root["subtitles"]![0]!.AsObject();
        var track = root["layers"]![0]!["tracks"]![0]!.AsObject();
        switch (field)
        {
            case "activeStyle":
            case "inactiveStyle":
                subtitle["karaoke"]![0]![field] = null;
                break;
            case "animationRanges":
                subtitle[field] = new JsonArray();
                break;
            case "textRangeId":
                track["target"]![field] = null;
                break;
            case "state":
                track["target"]![field] = "NORMAL";
                break;
            case "colorSpace":
                track[field] = "LINEAR_RGB";
                break;
            case "componentMask":
            case "mode":
                track["keyframes"] = new JsonArray();
                track["initialValue"] = 1;
                track["transforms"] = JsonNode.Parse("""
                    [{
                        "id": "60000000-0000-0000-0000-000000000001",
                        "start": { "numerator": 0, "denominator": 1 },
                        "end": { "numerator": 1, "denominator": 1 },
                        "value": 0.5,
                        "acceleration": 1
                    }]
                    """);
                var operation = track["transforms"]![0]!.AsObject();
                if (field == "componentMask")
                {
                    operation[field] = 0;
                }
                else
                {
                    operation[field] = "INTERPOLATE_TO";
                }
                break;
            case "FONT_SIZE":
                track["target"]!["property"] = field;
                track["keyframes"]![0]!["value"] = 64;
                break;
            case "SHADOW_OFFSET":
                track["target"]!["property"] = field;
                track["keyframes"]![0]!["value"] = new JsonObject { ["x"] = 2, ["y"] = 2 };
                break;
            default:
                root[field] = 0;
                break;
        }
        await AssertRejectedAtEveryEntry(root);
    }

    [Theory]
    [InlineData("combining")]
    [InlineData("surrogate")]
    [InlineData("zwj")]
    [InlineData("identity")]
    public async Task VersionElevenStillRejectsInvalidGraphemeBoundariesAndConflictingGroupIdentity(string invalidKind)
    {
        var root = VersionElevenProjectJsonFixture.Create(false);
        var subtitle = root["subtitles"]![0]!.AsObject();
        switch (invalidKind)
        {
            case "combining":
                subtitle["karaoke"]![0]!["utf16Length"] = 2;
                break;
            case "surrogate":
                subtitle["karaoke"]![0]!["utf16Length"] = 4;
                break;
            case "zwj":
                subtitle["inactiveKaraoke"]![0]!["utf16Length"] = 2;
                break;
            default:
                subtitle["inactiveKaraoke"]![0]!["id"] = subtitle["karaoke"]![0]!["id"]!.DeepClone();
                break;
        }
        await AssertRejectedAtEveryEntry(root);
    }

    private static void AssertPreserved(ProjectDocument document, bool includeRangeStyles)
    {
        Assert.Equal(ProjectDocument.CURRENT_VERSION, document.Version);
        Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000001"), document.Id);
        var line = Assert.Single(document.Subtitles);
        Assert.Equal(Guid.Parse("30000000-0000-0000-0000-000000000001"), line.Id);
        Assert.Equal("Ae\u0301😀👩‍💻Z", line.Text);
        Assert.Equal(MediaTime.Zero, line.Start);
        Assert.Equal(new MediaTime(6), line.End);
        Assert.Empty(line.AnimationRanges);
        Assert.Equal(2, line.Karaoke.Length);
        Assert.Equal(Guid.Parse("40000000-0000-0000-0000-000000000001"), line.Karaoke[0].Id);
        Assert.Equal(0, line.Karaoke[0].Utf16Start);
        Assert.Equal(5, line.Karaoke[0].Utf16Length);
        Assert.Equal(new MediaTime(1, 3), line.Karaoke[0].Start);
        Assert.Equal(new MediaTime(4, 3), line.Karaoke[0].End);
        Assert.Equal(KaraokeHighlightKind.SWEEP, line.Karaoke[0].HighlightKind);
        Assert.Equal(new SceneColor(4.123456789123, 0.25, 1, 0.625), line.Karaoke[0].HighlightColor);
        Assert.Equal(Guid.Parse("40000000-0000-0000-0000-000000000002"), line.Karaoke[1].Id);
        Assert.Equal(10, line.Karaoke[1].Utf16Start);
        Assert.Equal(1, line.Karaoke[1].Utf16Length);
        Assert.Equal(new MediaTime(7, 3), line.Karaoke[1].Start);
        Assert.Equal(new MediaTime(8, 3), line.Karaoke[1].End);
        Assert.Equal(new MediaTime(1), line.Karaoke[1].Start - line.Karaoke[0].End);
        Assert.Equal(KaraokeHighlightKind.OUTLINE_STEP, line.Karaoke[1].HighlightKind);
        Assert.Equal(new SceneColor(0, 1, 0), line.Karaoke[1].HighlightColor);

        var inactive = Assert.Single(line.InactiveKaraoke);
        Assert.Equal(Guid.Parse("40000000-0000-0000-0000-000000000003"), inactive.Id);
        Assert.Equal(5, inactive.Utf16Start);
        Assert.Equal(5, inactive.Utf16Length);
        Assert.Equal(new MediaTime(13, 7), inactive.Start);
        Assert.Equal(new MediaTime(29, 7), inactive.End);
        Assert.Equal(KaraokeHighlightKind.STEP, inactive.HighlightKind);
        Assert.Equal(new SceneColor(1, 0, 0, 0.5), inactive.HighlightColor);

        if (includeRangeStyles)
        {
            Assert.Equal(2, line.KaraokeStyleSpans.Length);
            var styled = line.KaraokeStyleSpans[0];
            Assert.Equal(0, styled.Utf16Start);
            Assert.Equal(5, styled.Utf16Length);
            Assert.Equal(new SceneColor(4.123456789123, 0.25, 1, 0.625), styled.ActiveStyle!.Fill!.Value);
            Assert.Equal(new ScenePoint(3.123456789123, -2.234567891234), styled.ActiveStyle.ShadowOffset!.Value);
            Assert.Equal(new SceneColor(0, 0, 0), styled.InactiveStyle!.Fill!.Value);
            Assert.Equal(0d, styled.InactiveStyle.StrokeWidth!.Value);
            var dormantStyle = line.KaraokeStyleSpans[1];
            Assert.Equal(5, dormantStyle.Utf16Start);
            Assert.Equal(5, dormantStyle.Utf16Length);
            Assert.Null(dormantStyle.ActiveStyle);
            Assert.Equal(new SceneColor(1, 0, 0, 0), dormantStyle.InactiveStyle!.Fill!.Value);
        }
        else
        {
            Assert.Empty(line.KaraokeStyleSpans);
        }

        var layer = Assert.Single(document.Layers);
        Assert.Equal(Guid.Parse("50000000-0000-0000-0000-000000000001"), layer.Id);
        Assert.Equal(line.Id, layer.SubtitleId);
        Assert.Equal(line.Start, layer.Start);
        Assert.Equal(line.End, layer.End);
        var track = Assert.Single(layer.Tracks);
        Assert.Equal(new AnimationTrackTarget(AnimationProperty.OPACITY), track.Target);
        var frame = Assert.Single(track.Keyframes);
        Assert.Equal(MediaTime.Zero, frame.Time);
        Assert.Equal(1d, frame.Value.Scalar);
    }

    private static async Task AssertRejectedAtEveryEntry(JsonObject root)
    {
        var bytes = Encoding.UTF8.GetBytes(root.ToJsonString());
        using var parsed = JsonDocument.Parse(bytes);
        using var directory = new TemporaryProjectDirectory();
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(bytes));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(parsed.RootElement));
        var path = Path.Combine(directory.Path, "invalid-version-eleven.aeginext");
        await File.WriteAllBytesAsync(path, bytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProjectStore.LoadAsync(path));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
    }
}
