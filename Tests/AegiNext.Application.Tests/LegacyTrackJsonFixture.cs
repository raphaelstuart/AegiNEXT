using System.Text.Json.Nodes;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

internal static class LegacyTrackJsonFixture
{
    internal static void DowngradeProject(JsonObject root)
    {
        if (root["version"]!.GetValue<int>() < 10)
        {
            LegacySubtitleAppearanceJsonFixture.Downgrade(root);
        }
        if (root["version"]!.GetValue<int>() >= 9 || root["tracks"] is not JsonArray tracks)
        {
            return;
        }

        root.Remove("tracks");
        root["subtitleTracks"] = tracks;
        var subtitles = root["subtitles"]!.AsArray().OfType<JsonObject>()
            .ToDictionary(line => line["id"]!.GetValue<Guid>());
        foreach (var clip in root["layers"]!.AsArray().OfType<JsonObject>())
        {
            if (clip["kind"]!.GetValue<string>() == "SUBTITLE")
            {
                var subtitleId = clip["subtitleId"]!.GetValue<Guid>();
                subtitles[subtitleId]["trackId"] = clip["trackId"]!.DeepClone();
            }
            clip.Remove("trackId");
            clip["children"] = new JsonArray();
        }

        if (root["timelineViewState"]?["collapsedAnimationRows"] is JsonArray rows)
        {
            foreach (var row in rows.OfType<JsonObject>())
            {
                row["scope"] = "SUBTITLE_TRACK";
            }
        }
    }
}
