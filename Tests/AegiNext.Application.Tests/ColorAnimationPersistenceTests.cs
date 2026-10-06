using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application.Tests;

public sealed class ColorAnimationPersistenceTests
{
    private static readonly JsonSerializerOptions wireOptions = CreateWireOptions();

    [Fact]
    public void LegacyColorGroupsMigrateAlongsideModernVectorsAndUseSubtitleStyleForMissingChannels()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "颜色");
        editor.UpdateSubtitle(id, line => line with { Style = line.Style with { Fill = new(-0.5, 4, 8, 0.7), Stroke = new(2, 3, 5, 0.9) } });
        editor.UpdateLayer(id, layer => layer with { Fill = new(11, 12, 13), Stroke = new(14, 15, 16) });
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(20, 30)));
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        var fillRed = new AnimationTrack(AnimationProperty.FILL_RED,
            [new(new(0), -2, KeyframeInterpolation.EASE_IN), new(new(4), 6)]);
        var fillAlpha = new AnimationTrack(AnimationProperty.FILL_ALPHA,
            [new(new(1), 0.2, KeyframeInterpolation.EASE_OUT), new(new(2), 0.8, KeyframeInterpolation.HOLD), new(new(3), 0.5)]);
        var strokeGreen = new AnimationTrack(AnimationProperty.STROKE_GREEN,
            [new(new(0), -1, KeyframeInterpolation.EASE_IN_OUT), new(new(4), 7)]);
        foreach (var track in new[] { fillRed, fillAlpha, strokeGreen })
        {
            root["layers"]![0]!["tracks"]!.AsArray().Add(JsonSerializer.SerializeToNode(track, wireOptions));
        }
        LegacyProjectJsonFixture.Downgrade(root, 4);

        var loaded = ProjectStore.Deserialize(Encode(root));
        Assert.Equal(ProjectDocument.CURRENT_VERSION, loaded.Version);
        Assert.Equal(3, loaded.Layers[0].Tracks.Length);
        var fill = loaded.Layers[0].Tracks.Single(track => track.Property == AnimationProperty.FILL);
        var stroke = loaded.Layers[0].Tracks.Single(track => track.Property == AnimationProperty.STROKE);
        for (var index = 0; index <= 400; index++)
        {
            var time = new MediaTime(index, 100);
            var actual = SceneEvaluator.EvaluateColorTrack(fill, time);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(fillRed, time), actual.Red, 9);
            Assert.Equal(4, actual.Green);
            Assert.Equal(8, actual.Blue);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(fillAlpha, time), actual.Alpha, 9);
            var actualStroke = SceneEvaluator.EvaluateColorTrack(stroke, time);
            Assert.Equal(2, actualStroke.Red);
            Assert.Equal(SceneEvaluator.EvaluateScalarTrack(strokeGreen, time), actualStroke.Green, 9);
            Assert.Equal(5, actualStroke.Blue);
            Assert.Equal(0.9, actualStroke.Alpha);
        }

        var saved = ProjectStore.Serialize(loaded);
        Assert.DoesNotContain("FILL_RED", Encoding.UTF8.GetString(saved), StringComparison.Ordinal);
        Assert.Equal(saved, ProjectStore.Serialize(ProjectStore.Deserialize(saved)));
    }

    [Fact]
    public void LegacyRawPresetMissingChannelsResolveAgainstEachApplicationTargetAndUndoOnce()
    {
        var editor = new ProjectEditor();
        var first = editor.AddSubtitle(new(0), new(4), "one");
        var second = editor.AddSubtitle(new(4), new(8), "two");
        editor.UpdateSubtitle(first, line => line with { Style = line.Style with { Fill = new(2, 3, 4, 0.5) } });
        editor.UpdateSubtitle(second, line => line with { Style = line.Style with { Fill = new(5, 6, 7, 0.8) } });
        var preset = new EffectPreset(Guid.NewGuid(), "old red",
            [new(AnimationProperty.FILL_RED, [new(new(0), -1, KeyframeInterpolation.EASE_OUT), new(new(4), 9)])]);
        editor.Apply("Add legacy preset", document => document with { Presets = [preset] });
        var loaded = ProjectStore.Deserialize(ProjectStore.Serialize(editor.Snapshot));
        Assert.Equal(AnimationProperty.FILL_RED, Assert.Single(loaded.Presets[0].Tracks).Property);
        editor = new(loaded);
        var before = editor.Snapshot;
        editor.ApplyPreset(first, loaded.Presets[0]);
        var firstValue = editor.Snapshot.Layers.Single(layer => layer.Id == first).Tracks[0].Keyframes[0].Value.Color;
        Assert.Equal(new SceneColor(-1, 3, 4, 0.5), firstValue);
        Assert.True(editor.Undo());
        Assert.Same(before, editor.Snapshot);
        Assert.False(editor.CanUndo);
        editor.ApplyPreset(second, loaded.Presets[0]);
        var secondValue = editor.Snapshot.Layers.Single(layer => layer.Id == second).Tracks[0].Keyframes[0].Value.Color;
        Assert.Equal(new SceneColor(-1, 6, 7, 0.8), secondValue);
        var saved = ProjectStore.Serialize(editor.Snapshot);
        Assert.Equal(saved, ProjectStore.Serialize(ProjectStore.Deserialize(saved)));
    }

    [Fact]
    public void SameGroupNewLegacyDuplicatesAndMalformedColorOrComponentCurvesFailAtomically()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "strict color");
        editor.SetKeyframe(id, AnimationProperty.FILL, new(new(0), new SceneColor(2, 4, 6, 0.8)));
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        root["layers"]![0]!["tracks"]!.AsArray().Add(JsonSerializer.SerializeToNode(new AnimationTrack(AnimationProperty.FILL_RED,
            [new(new(0), 1)]), wireOptions));
        LegacyProjectJsonFixture.Downgrade(root, 4);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(root)));
        root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        root["layers"]![0]!["tracks"]![0]!["keyframes"]![0]!["value"]!.AsObject().Remove("alpha");
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(root)));
        root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        root["layers"]![0]!["tracks"]![0]!["keyframes"]![0]!["componentCurves"] = new JsonArray((JsonNode?)null);
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(root)));
    }

    [Fact]
    public void OldVectorCurveFieldMigratesWithoutIntroducingDualCurveAuthority()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "old vector phase");
        editor.SetKeyframe(id, AnimationProperty.POSITION, new(new(0), new ScenePoint(20, 30), KeyframeInterpolation.EASE_IN)
        {
            VectorCurve = new(KeyframeInterpolation.EASE_OUT, 0.2, 0.8)
        });
        var root = JsonNode.Parse(ProjectStore.Serialize(editor.Snapshot))!.AsObject();
        LegacyProjectJsonFixture.Downgrade(root, 4);
        var key = root["layers"]![0]!["tracks"]![0]!["keyframes"]![0]!.AsObject();
        key["vectorCurve"] = key["componentCurves"]![0]!.DeepClone();
        key.Remove("componentCurves");
        var loaded = ProjectStore.Deserialize(Encode(root));
        Assert.Equal(new AnimationCurve(KeyframeInterpolation.EASE_OUT, 0.2, 0.8), loaded.Layers[0].Tracks[0].Keyframes[0].GetCurve(1));
        var output = Encoding.UTF8.GetString(ProjectStore.Serialize(loaded));
        Assert.DoesNotContain("vectorCurve", output, StringComparison.Ordinal);
        Assert.Contains("componentCurves", output, StringComparison.Ordinal);
        key["componentCurves"] = new JsonArray();
        Assert.Throws<InvalidDataException>(() => ProjectStore.Deserialize(Encode(root)));
    }

    [Fact]
    public void WorkerStyleJsonAndStrictProjectStoreRoundTripAllThreeValueKindsAndIndependentColorCurves()
    {
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(0), new(4), "wire color");
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.6));
        editor.SetKeyframe(id, AnimationProperty.SCALE, new(new(0), new ScenePoint(2, 3)));
        editor.SetKeyframe(id, AnimationProperty.FILL, new(new(0), new SceneColor(-2, 4, 7, 0.5), KeyframeInterpolation.EASE_IN)
        {
            ComponentCurves = [new(KeyframeInterpolation.EASE_OUT), null, new(KeyframeInterpolation.HOLD)]
        });
        var bytes = JsonSerializer.SerializeToUtf8Bytes(editor.Snapshot, wireOptions);
        Assert.Equal(ProjectStore.Serialize(editor.Snapshot), ProjectStore.Serialize(ProjectStore.Deserialize(bytes)));
    }

    private static JsonSerializerOptions CreateWireOptions()
    {
        var result = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        result.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return result;
    }

    private static byte[] Encode(JsonObject root) => Encoding.UTF8.GetBytes(root.ToJsonString());
}
