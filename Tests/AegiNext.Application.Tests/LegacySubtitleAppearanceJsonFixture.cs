using System.Text.Json.Nodes;

namespace AegiNext.Application.Tests;

internal static class LegacySubtitleAppearanceJsonFixture
{
    internal static void Downgrade(JsonNode? node)
    {
        if (node is JsonObject value)
        {
            value.Remove("letterSpacing");
            value.Remove("wrapMode");
            value.Remove("fillBlur");
            value.Remove("strokeBlur");
            foreach (var item in value)
            {
                Downgrade(item.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                Downgrade(item);
            }
        }
    }
}
