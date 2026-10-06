using System.Text.Json.Nodes;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

internal static class SubtitleContentJsonMigration
{
    internal static void UpgradeProject(JsonObject document, int version)
    {
        if (version != 3)
        {
            return;
        }
        document["version"] = ProjectDocument.CURRENT_VERSION;
        AddDecorations(document);
        if (document["subtitles"] is not JsonArray lines)
        {
            return;
        }
        foreach (var line in lines.OfType<JsonObject>())
        {
            line.TryAdd("inlineSpans", new JsonArray());
            if (line["karaoke"] is not JsonArray clips)
            {
                continue;
            }
            foreach (var clip in clips.OfType<JsonObject>())
            {
                clip.TryAdd("id", JsonValue.Create(Guid.NewGuid()));
                clip.TryAdd("highlightKind", JsonValue.Create(nameof(KaraokeHighlightKind.SWEEP)));
                clip.TryAdd("inactiveStyle", null);
                clip.TryAdd("activeStyle", null);
            }
        }
    }

    internal static void UpgradeStyleLibrary(JsonObject collection, int version)
    {
        if (version is not (1 or 2 or 3))
        {
            return;
        }
        collection["version"] = SubtitleStylePresetCollection.CURRENT_VERSION;
        if (version is 1 or 2)
        {
            AddDecorations(collection);
        }
        if (collection["presets"] is JsonArray presets)
        {
            foreach (var preset in presets.OfType<JsonObject>())
            {
                preset.TryAdd("timingPostProcessor", null);
            }
        }
    }

    private static void AddDecorations(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            if (value.ContainsKey("fontFamily") && value.ContainsKey("fontSize"))
            {
                value.TryAdd("underline", JsonValue.Create(false));
                value.TryAdd("strikethrough", JsonValue.Create(false));
            }
            foreach (var property in value)
            {
                AddDecorations(property.Value);
            }
        }
        else if (node is JsonArray values)
        {
            foreach (var item in values)
            {
                AddDecorations(item);
            }
        }
    }
}
