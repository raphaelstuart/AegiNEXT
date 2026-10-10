using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleAnimationJsonMigration
{
    internal static void RejectUndeclaredFields(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value.ContainsKey("animationRanges") || value.ContainsKey("textRangeId") || value.ContainsKey("state") ||
                value.ContainsKey("colorSpace") || value.ContainsKey("componentMask") || value.ContainsKey("mode"))
            {
                throw new JsonException("旧工程不能包含未声明版本的文字范围、状态或插值方式。");
            }
            if (value["property"] is JsonValue property && property.TryGetValue<string>(out var name) &&
                Enum.TryParse<AnimationProperty>(name, true, out var parsed) &&
                parsed is AnimationProperty.FONT_SIZE or AnimationProperty.SHADOW_OFFSET or AnimationProperty.SHADOW_BLUR or AnimationProperty.SHADOW_COLOR)
            {
                throw new JsonException("旧工程不能包含未声明版本的字号或阴影动画。");
            }
            foreach (var item in value)
            {
                RejectUndeclaredFields(item.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RejectUndeclaredFields(item);
            }
        }
    }
}
