using System.Text;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.Json.Serialization;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class VectorAnimationPersistenceTests
{
    private static readonly string[] transformFields = ["position", "scale", "pivot", "rotation"];
    private static readonly JsonSerializerOptions wireOptions = CreateWireOptions();

    [Fact]
    public void WorkerStyleSerializerOutputCanBeLoadedByTheStrictProjectStoreWithoutCallerConverters()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(1, 3), new(13, 3), "wire Vector");
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(1, 3), new ScenePoint(10, -20), KeyframeInterpolation.EASE_IN)
        {
            CurveStart = 0.1, CurveEnd = 0.9, VectorCurve = new(KeyframeInterpolation.EASE_OUT, 0.2, 0.8)
        });
        editor.SetKeyframe(id, AnimationProperty.SCALE, new(new(1), new ScenePoint(2, 3)));
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1), 0.75));
        var wire = JsonSerializer.SerializeToElement(editor.Snapshot, wireOptions);
        var loaded = ProjectStore.Deserialize(Encoding.UTF8.GetBytes(wire.GetRawText()));
        Assert.Equal(ProjectStore.Serialize(editor.Snapshot), ProjectStore.Serialize(loaded));
        Assert.Equal(new MediaTime(1, 3), loaded.Subtitles[0].Start);
        Assert.Equal(new ScenePoint(10, -20), loaded.Layers[0].Tracks[0].Keyframes[0].Value.Vector);
        Assert.Equal(new AnimationCurve(KeyframeInterpolation.EASE_OUT, 0.2, 0.8), loaded.Layers[0].Tracks[0].Keyframes[0].VectorCurve);
    }

    [Fact]
    public void VectorAnimationRoundTripUsesOnePropertyAndOneValueAndUndoRestoresTheSnapshot()
    {
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "Vector");
        var before = initial.Snapshot;
        var editor = new ProjectEditor(before);
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(12, -34), KeyframeInterpolation.EASE_IN));
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(4), new ScenePoint(90, 100)));
        var encoded = ProjectStore.Serialize(editor.Snapshot);
        var root = JsonNode.Parse(encoded)!.AsObject();
        var transform = root["layers"]![0]!["transform"]!.AsObject();
        Assert.Equal(transformFields, transform.Select(pair => pair.Key));
        Assert.Single(root["layers"]![0]!["tracks"]!.AsArray());
        Assert.Equal("POSITION", root["layers"]![0]!["tracks"]![0]!["target"]!["property"]!.GetValue<string>());
        Assert.Equal(2, root["layers"]![0]!["tracks"]![0]!["keyframes"]![0]!["value"]!.AsObject().Count);
        var loaded = ProjectStore.Deserialize(encoded);
        Assert.Equal(encoded, ProjectStore.Serialize(loaded));
        Assert.Equal(new ScenePoint(31.5, -0.5), Assert.Single(SceneEvaluator.Evaluate(loaded, new(2))).Transform.Position);
        Assert.True(editor.Undo());
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
    }

    [Fact]
    public void LegacyIndependentAxesMigrateWithoutChangingAnyCurveSampleIncludingAfterCropping()
    {
        var x = new AnimationTrack(AnimationProperty.POSITION_X,
            [new(new(0), 10, KeyframeInterpolation.EASE_IN_OUT), new(new(4), 210)]);
        var y = new AnimationTrack(AnimationProperty.POSITION_Y,
            [new(new(1), -50, KeyframeInterpolation.EASE_OUT), new(new(2), 30, KeyframeInterpolation.EASE_IN), new(new(3), 150)]);
        var legacy = MakeLegacy(x, y);
        var loaded = ProjectStore.Deserialize(Encode(legacy));
        var layer = Assert.Single(loaded.Layers);
        var track = Assert.Single(layer.Tracks);
        Assert.Equal(AnimationProperty.POSITION, track.Property);
        Assert.Equal(new ScenePoint(100, 200), layer.Transform.Position);
        Assert.Equal(new ScenePoint(2, 3), layer.Transform.Scale);
        Assert.Equal(new ScenePoint(7, 9), layer.Transform.Pivot);
        var cropped = LayerAnimationTiming.Clip(layer with { Start = new(1), End = new(3), AnimationOffset = new(1) });
        for (var index = 0; index <= 400; index++)
        {
            var time = new MediaTime(index, 100);
            var actual = SceneEvaluator.EvaluateVectorTrack(track, time);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(x, time), actual.X, 9);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(y, time), actual.Y, 9);
            if (time >= new MediaTime(1) && time <= new MediaTime(3))
            {
                var trimmed = SceneEvaluator.EvaluateVectorTrack(Assert.Single(cropped.Tracks), time);
                Assert.Equal(actual.X, trimmed.X, 9);
                Assert.Equal(actual.Y, trimmed.Y, 9);
            }
        }

        var encoded = ProjectStore.Serialize(loaded);
        Assert.DoesNotContain("POSITION_X", Encoding.UTF8.GetString(encoded), StringComparison.Ordinal);
        Assert.DoesNotContain("POSITION_Y", Encoding.UTF8.GetString(encoded), StringComparison.Ordinal);
        Assert.Equal(encoded, ProjectStore.Serialize(ProjectStore.Deserialize(encoded)));
    }

    [Fact]
    public void LegacySingleAxisUsesTheOtherBaseComponentAndPresetScaleUsesOne()
    {
        var legacy = MakeLegacy(new AnimationTrack(AnimationProperty.POSITION_Y, [new(new(0), 50), new(new(4), 70)]));
        var loaded = ProjectStore.Deserialize(Encode(legacy));
        Assert.Equal(new ScenePoint(100, 60), SceneEvaluator.EvaluateVectorTrack(Assert.Single(loaded.Layers[0].Tracks), new(2)));
        legacy["presets"] = new JsonArray(new JsonObject
        {
            ["id"] = Guid.NewGuid().ToString(), ["name"] = "old scale", ["tracks"] = new JsonArray(Track(new(AnimationProperty.SCALE_X,
                [new(new(0), 0.2), new(new(4), 1)]))), ["motionPath"] = null, ["mask"] = null, ["blend"] = "NORMAL"
        });
        loaded = ProjectStore.Deserialize(Encode(legacy));
        Assert.Equal(AnimationProperty.SCALE, Assert.Single(loaded.Presets[0].Tracks).Property);
        Assert.Equal(new ScenePoint(0.2, 1), loaded.Presets[0].Tracks[0].Keyframes[0].Value.Vector);
    }

    [Fact]
    public void MixedLegacyVectorDuplicateAndIncompleteValuesAreRejectedInsteadOfSilentlyRepaired()
    {
        var legacy = MakeLegacy(new AnimationTrack(AnimationProperty.POSITION_X, [new(new(0), 20)]));
        legacy["layers"]![0]!["transform"]!["position"] = new JsonObject { ["x"] = 1, ["y"] = 2 };
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(legacy)));
        legacy = MakeLegacy(new AnimationTrack(AnimationProperty.POSITION_X, [new(new(0), 20)]));
        legacy["layers"]![0]!["tracks"]!.AsArray().Add(Track(new(AnimationProperty.POSITION_X, [new(new(0), 30)])));
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(legacy)));
        legacy = MakeLegacy(new AnimationTrack(AnimationProperty.POSITION_X, [new(new(0), 20)]));
        legacy["layers"]![0]!["tracks"]!.AsArray().Add(new JsonObject
        {
            ["property"] = "POSITION", ["keyframes"] = new JsonArray(new JsonObject
            {
                ["time"] = Time(new(0)), ["value"] = new JsonObject { ["x"] = 1, ["y"] = 2 },
                ["interpolation"] = "LINEAR", ["curveStart"] = 0, ["curveEnd"] = 1
            })
        });
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(legacy)));
        var initial = new ProjectEditor();
        var id = initial.AddSubtitle(new(0), new(4), "strict");
        initial.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(1, 2)));
        var current = JsonNode.Parse(ProjectStore.Serialize(initial.Snapshot))!.AsObject();
        current["layers"]![0]!["tracks"]![0]!["keyframes"]![0]!["value"]!.AsObject().Remove("y");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(current)));
    }

    private static JsonSerializerOptions CreateWireOptions()
    {
        var result = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, MaxDepth = 128 };
        result.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return result;
    }

    private static JsonObject MakeLegacy(params AnimationTrack[] tracks)
    {
        var editor = new ProjectEditor();
        editor.AddSubtitle(new(0), new(4), "legacy axes");
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        root["layers"]![0]!["transform"] = new JsonObject
        {
            ["x"] = 100, ["y"] = 200, ["scaleX"] = 2, ["scaleY"] = 3,
            ["rotation"] = 5, ["anchorX"] = 7, ["anchorY"] = 9
        };
        root["layers"]![0]!["tracks"] = new JsonArray(tracks.Select(Track).ToArray());
        LegacyProjectJsonFixture.Downgrade(root, 4);
        return root;
    }

    private static JsonNode Track(AnimationTrack track) => new JsonObject
    {
        ["property"] = track.Property.ToString(),
        ["keyframes"] = new JsonArray(track.Keyframes.Select(frame => (JsonNode)new JsonObject
        {
            ["time"] = Time(frame.Time), ["value"] = frame.Value.Scalar, ["interpolation"] = frame.Interpolation.ToString(),
            ["curveStart"] = frame.CurveStart, ["curveEnd"] = frame.CurveEnd
        }).ToArray())
    };

    private static JsonObject Time(MediaTime time) => new() { ["numerator"] = time.Numerator, ["denominator"] = time.Denominator };
    private static byte[] Encode(JsonObject value) => Encoding.UTF8.GetBytes(value.ToJsonString());
}
