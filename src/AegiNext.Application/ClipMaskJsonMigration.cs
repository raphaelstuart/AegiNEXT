using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class ClipMaskJsonMigration
{
    internal static void RejectCurrentLegacyFields(JsonObject document)
    {
        if (document["layers"] is JsonArray layers)
        {
            RejectLayerLegacyFields(layers);
        }

        if (document["presets"] is JsonArray presets)
        {
            foreach (var preset in presets.OfType<JsonObject>())
            {
                RejectTrackLegacyFields(preset);
            }
        }
    }

    internal static void UpgradeLegacy(JsonObject document)
    {
        if (document["layers"] is JsonArray layers)
        {
            UpgradeLayers(layers, "layers");
        }

        if (document["presets"] is JsonArray presets)
        {
            for (var index = 0; index < presets.Count; index++)
            {
                if (presets[index] is not JsonObject preset)
                {
                    continue;
                }

                var location = $"presets[{index}]";
                RejectNonemptyMask(preset, location);
                preset.Remove("mask");
                UpgradeTracks(preset, location);
            }
        }

        document["version"] = ProjectDocument.CURRENT_VERSION;
    }

    private static void UpgradeLayers(JsonArray layers, string location)
    {
        for (var index = 0; index < layers.Count; index++)
        {
            if (layers[index] is not JsonObject layer)
            {
                continue;
            }

            var layerLocation = $"{location}[{index}]";
            RejectNonemptyMask(layer, layerLocation);
            layer.TryAdd("mask", null);
            UpgradeTracks(layer, layerLocation);
            if (layer["children"] is JsonArray children)
            {
                UpgradeLayers(children, $"{layerLocation}.children");
            }
        }
    }

    private static void RejectLayerLegacyFields(JsonArray layers)
    {
        foreach (var layer in layers.OfType<JsonObject>())
        {
            if (layer["transform"] is JsonObject transform &&
                transform.Any(pair => pair.Key is "x" or "y" or "scaleX" or "scaleY" or "anchorX" or "anchorY"))
            {
                throw new JsonException("版本 5 不接受旧标量图层变换字段。");
            }

            RejectTrackLegacyFields(layer);
            if (layer["children"] is JsonArray children)
            {
                RejectLayerLegacyFields(children);
            }
        }
    }

    private static void RejectTrackLegacyFields(JsonObject owner)
    {
        if (owner["tracks"] is not JsonArray tracks)
        {
            return;
        }

        foreach (var track in tracks.OfType<JsonObject>())
        {
            if (track.ContainsKey("property"))
            {
                throw new JsonException("版本 5 的动画轨道只接受完整 target，不接受旧 property 字段。");
            }

            if (track["keyframes"] is JsonArray frames && frames.OfType<JsonObject>().Any(frame => frame.ContainsKey("vectorCurve")))
            {
                throw new JsonException("版本 5 的关键帧不接受旧 vectorCurve 字段。");
            }
        }
    }

    private static void RejectNonemptyMask(JsonObject owner, string location)
    {
        if (owner["mask"] is not null)
        {
            var id = owner["id"] is JsonValue value && value.TryGetValue<string>(out var identifier)
                ? identifier : "未知标识";
            throw new InvalidDataException($"{location}（{id}）含有非空旧局部蒙版，无法安全升级为项目坐标 Clip 蒙版；原文件未修改。");
        }
    }

    private static void UpgradeTracks(JsonObject owner, string location)
    {
        if (owner["tracks"] is not JsonArray tracks)
        {
            return;
        }

        for (var index = 0; index < tracks.Count; index++)
        {
            if (tracks[index] is not JsonObject track)
            {
                continue;
            }

            if (track.ContainsKey("target") || track["property"] is not JsonValue value ||
                !value.TryGetValue<string>(out var property))
            {
                throw new JsonException($"{location}.tracks[{index}] 缺少旧属性字段，或同时包含新旧轨道目标。");
            }

            track["target"] = new JsonObject { ["property"] = property, ["nodeId"] = null };
            track.Remove("property");
        }
    }
}
