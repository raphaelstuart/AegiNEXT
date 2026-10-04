using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegiNext.Application;

internal static class SubtitlePositionJsonMigration
{
    internal static JsonObject? UpgradeVersionOne(JsonElement root, string itemsName)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("version", out var version) ||
            version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var number))
        {
            throw new JsonException("缺少完整的文件版本。");
        }

        if (number != 1)
        {
            return null;
        }

        var upgraded = JsonNode.Parse(root.GetRawText(), documentOptions: new() { MaxDepth = 128 })!.AsObject();
        upgraded["version"] = 2;
        if (upgraded[itemsName] is JsonArray items)
        {
            foreach (var item in items)
            {
                if (item is JsonObject value && value["style"] is JsonObject style)
                {
                    if (style.ContainsKey("position"))
                    {
                        throw new JsonException("第一版字幕样式不能包含第二版位置字段。");
                    }

                    style["position"] = null;
                }
            }
        }

        if (itemsName == "subtitles")
        {
            AddCurveRanges(upgraded);
        }

        return upgraded;
    }

    private static void AddCurveRanges(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value["keyframes"] is JsonArray keyframes)
            {
                foreach (var item in keyframes.OfType<JsonObject>())
                {
                    if (item.ContainsKey("curveStart") || item.ContainsKey("curveEnd"))
                    {
                        throw new JsonException("第一版关键帧不能包含第二版曲线字段。");
                    }

                    item["curveStart"] = 0;
                    item["curveEnd"] = 1;
                }
            }

            foreach (var property in value)
            {
                AddCurveRanges(property.Value);
            }
        }
        else if (node is JsonArray values)
        {
            foreach (var item in values)
            {
                AddCurveRanges(item);
            }
        }
    }
}
