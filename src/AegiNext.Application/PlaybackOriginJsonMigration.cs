using System.Text.Json;
using System.Text.Json.Nodes;

namespace AegiNext.Application;

internal static class PlaybackOriginJsonMigration
{
    private const int PLAYBACK_ORIGIN_VERSION = 6;

    internal static void Upgrade(JsonObject document, int sourceVersion)
    {
        if (sourceVersion >= PLAYBACK_ORIGIN_VERSION)
        {
            return;
        }
        if (document["media"] is JsonObject media)
        {
            if (media.ContainsKey("playbackOrigin"))
            {
                throw new JsonException("旧工程不能包含未声明版本的播放零点字段。");
            }
            media.Add("playbackOrigin", null);
        }
    }
}
