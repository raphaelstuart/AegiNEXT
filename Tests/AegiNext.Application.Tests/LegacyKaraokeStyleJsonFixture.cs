using System.Text.Json.Nodes;

namespace AegiNext.Application.Tests;

internal static class LegacyKaraokeStyleJsonFixture
{
    internal static void Downgrade(JsonObject root)
    {
        if (root["version"]!.GetValue<int>() >= 11 || root["subtitles"] is not JsonArray lines)
        {
            return;
        }
        foreach (var line in lines.OfType<JsonObject>())
        {
            var spans = line["karaokeStyleSpans"] as JsonArray;
            foreach (var field in new[] { "karaoke", "inactiveKaraoke" })
            {
                if (line[field] is not JsonArray segments)
                {
                    continue;
                }
                foreach (var segment in segments.OfType<JsonObject>())
                {
                    var offset = segment["utf16Start"]!.GetValue<int>();
                    var span = spans?.OfType<JsonObject>().FirstOrDefault(value =>
                        value["utf16Start"]!.GetValue<int>() <= offset &&
                        offset < value["utf16Start"]!.GetValue<int>() + value["utf16Length"]!.GetValue<int>());
                    segment.TryAdd("activeStyle", span?["activeStyle"]?.DeepClone());
                    segment.TryAdd("inactiveStyle", span?["inactiveStyle"]?.DeepClone());
                }
            }
            line.Remove("karaokeStyleSpans");
        }
    }
}
