using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

internal static class FlatClipJsonMigration
{
    internal static void Upgrade(JsonObject document, JsonSerializerOptions options)
    {
        if (document.ContainsKey("tracks") || document["subtitleTracks"] is not JsonArray tracks ||
            document["subtitles"] is not JsonArray subtitles || document["layers"] is not JsonArray layers)
        {
            throw new JsonException("旧工程缺少字幕轨道、字幕或合成层，或同时包含新旧轨道字段。");
        }
        var trackIds = tracks.Select(track => RequiredId(track as JsonObject, "id")).ToHashSet();
        if (trackIds.Count != tracks.Count)
        {
            throw new JsonException("旧工程轨道标识重复。");
        }
        var subtitleTracks = new Dictionary<Guid, Guid>();
        foreach (var node in subtitles)
        {
            var line = node as JsonObject ?? throw new JsonException("旧字幕不能为 null。");
            var subtitleId = RequiredId(line, "id");
            var trackId = RequiredId(line, "trackId");
            if (!subtitleTracks.TryAdd(subtitleId, trackId) || !trackIds.Contains(trackId))
            {
                throw new JsonException("旧字幕标识重复或引用不存在的轨道。");
            }
            line.Remove("trackId");
        }

        var flattened = new JsonArray();
        var sceneTracks = new Dictionary<Guid, Guid>();
        var groups = new HashSet<Guid>();
        var layerIds = new HashSet<Guid>();
        foreach (var node in layers)
        {
            Flatten(node as JsonObject ?? throw new JsonException("旧图层不能为 null。"), 0,
                flattened, tracks, trackIds, subtitleTracks, sceneTracks, groups, layerIds, options);
        }
        var ownership = ArrangeTracks(flattened, tracks, trackIds, options);
        document.Remove("subtitleTracks");
        document["tracks"] = ownership.Tracks;
        document["layers"] = flattened;
        UpgradeViewState(document["timelineViewState"] as JsonObject, ownership.Owners, sceneTracks, groups);
    }

    private static void Flatten(JsonObject layer, int depth, JsonArray flattened, JsonArray tracks,
        HashSet<Guid> trackIds, Dictionary<Guid, Guid> subtitleTracks, Dictionary<Guid, Guid> sceneTracks,
        HashSet<Guid> groups, HashSet<Guid> layerIds, JsonSerializerOptions options)
    {
        var id = RequiredId(layer, "id");
        if (depth > 32 || !layerIds.Add(id) || layerIds.Count > 10000 ||
            layer.ContainsKey("trackId") || layer["children"] is not JsonArray children ||
            layer["kind"] is not JsonValue kindValue || !kindValue.TryGetValue<string>(out var kind))
        {
            throw new JsonException("旧合成树过深、过大、标识重复或字段无效。");
        }
        if (kind == "GROUP")
        {
            var metadata = layer.DeepClone().AsObject();
            metadata.Remove("children");
            metadata["kind"] = "SHAPE";
            metadata["trackId"] = ProjectTrack.DEFAULT_TRACK_ID;
            var group = metadata.Deserialize<ProjectLayer>(options) ?? throw new JsonException("旧组不能为 null。");
            if (group.SubtitleId is not null || group.Shape is not null || group.Image is not null)
            {
                throw new JsonException("旧组不能包含字幕、形状或图片载荷。");
            }
            if (group.Transform != new LayerTransform() || !group.Opacity.Equals(1d) ||
                group.Blend != BlendMode.NORMAL || group.Blur != 0 || !group.Tracks.IsEmpty ||
                group.MotionPath is not null || group.Mask is not null)
            {
                throw CannotFlatten(group);
            }
            ProjectValidator.Validate(new() { Layers = [group with { Shape = new(ShapeKind.RECTANGLE, 1, 1) }] });
            foreach (var childNode in children)
            {
                var child = childNode as JsonObject ?? throw new JsonException("旧组子节点不能为 null。");
                var start = child["start"]?.Deserialize<MediaTime>(options) ?? throw new JsonException("旧组子节点缺少开始时间。");
                var end = child["end"]?.Deserialize<MediaTime>(options) ?? throw new JsonException("旧组子节点缺少结束时间。");
                var blend = child["blend"]?.Deserialize<BlendMode>(options) ?? throw new JsonException("旧组子节点缺少混合方式。");
                if (start < group.Start || end > group.End || blend != BlendMode.NORMAL)
                {
                    throw CannotFlatten(group);
                }
            }
            groups.Add(id);
            foreach (var child in children)
            {
                Flatten(child!.AsObject(), depth + 1, flattened, tracks, trackIds, subtitleTracks,
                    sceneTracks, groups, layerIds, options);
            }
            return;
        }
        if (children.Count != 0)
        {
            throw new JsonException("只有旧图层组可以包含子节点。");
        }
        var clip = layer.DeepClone().AsObject();
        clip.Remove("children");
        if (kind == "SUBTITLE")
        {
            var subtitleId = RequiredId(layer, "subtitleId");
            if (!subtitleTracks.TryGetValue(subtitleId, out var trackId))
            {
                throw new JsonException("旧字幕层引用不存在的字幕。");
            }
            clip["trackId"] = trackId;
        }
        else
        {
            var trackId = CreateSceneTrackId(id, trackIds);
            sceneTracks.Add(id, trackId);
            var track = new ProjectTrack { Id = trackId, Name = CreateSceneTrackName(layer["name"]?.GetValue<string>() ?? "Clip") };
            tracks.Add(JsonSerializer.SerializeToNode(track, options));
            clip["trackId"] = trackId;
        }
        flattened.Add(clip);
    }

    private static (JsonArray Tracks, Dictionary<Guid, List<Guid>> Owners) ArrangeTracks(JsonArray clips,
        JsonArray legacyTracks, HashSet<Guid> reserved, JsonSerializerOptions options)
    {
        var originals = legacyTracks.OfType<JsonObject>().ToDictionary(track => RequiredId(track, "id"));
        var owners = originals.Keys.ToDictionary(id => id, _ => new List<Guid>());
        var drawingTracks = new List<JsonObject>();
        var used = new HashSet<Guid>();
        Guid? previousOriginal = null;
        Guid? previousAssigned = null;
        MediaTime previousEnd = default;
        foreach (var node in clips)
        {
            var clip = node!.AsObject();
            var originalId = RequiredId(clip, "trackId");
            var start = clip["start"]!.Deserialize<MediaTime>(options);
            var end = clip["end"]!.Deserialize<MediaTime>(options);
            Guid assigned;
            if (previousOriginal == originalId && start >= previousEnd)
            {
                assigned = previousAssigned!.Value;
            }
            else
            {
                var track = originals[originalId].DeepClone().AsObject();
                assigned = used.Add(originalId) ? originalId : CreateSceneTrackId(RequiredId(clip, "id"), reserved);
                track["id"] = assigned;
                drawingTracks.Add(track);
                owners[originalId].Add(assigned);
            }
            clip["trackId"] = assigned;
            previousOriginal = originalId;
            previousAssigned = assigned;
            previousEnd = end;
        }
        foreach (var pair in originals.Where(pair => !used.Contains(pair.Key)))
        {
            drawingTracks.Add(pair.Value.DeepClone().AsObject());
            owners[pair.Key].Add(pair.Key);
        }
        return (new JsonArray(drawingTracks.AsEnumerable().Reverse().Select(track => (JsonNode)track).ToArray()), owners);
    }

    private static InvalidDataException CannotFlatten(ProjectLayer group)
    {
        return new($"旧图层组“{group.Name}”（{group.Id}）包含变换、隔离合成或时间裁剪，无法安全展开为轨道片段；原文件未修改。");
    }

    private static Guid RequiredId(JsonObject? owner, string property)
    {
        if (owner?[property] is not JsonValue value || !value.TryGetValue<Guid>(out var id) || id == Guid.Empty)
        {
            throw new JsonException($"旧工程缺少有效 {property} 标识。");
        }
        return id;
    }

    private static string CreateSceneTrackName(string clipName)
    {
        var clean = new string(clipName.Where(character => !char.IsControl(character)).ToArray()).Trim();
        if (clean.Length == 0)
        {
            return "Clip";
        }
        var enumerator = StringInfo.GetTextElementEnumerator(clean);
        var end = 0;
        while (enumerator.MoveNext())
        {
            var next = enumerator.ElementIndex + enumerator.GetTextElement().Length;
            if (next > 128)
            {
                break;
            }
            end = next;
        }
        return end == 0 ? "Clip" : clean[..end];
    }

    private static Guid CreateSceneTrackId(Guid layerId, HashSet<Guid> reserved)
    {
        for (var salt = 0; ; salt++)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"AegiNext.ProjectTrack.{layerId:D}.{salt}"));
            var id = new Guid(bytes.AsSpan(0, 16));
            if (id != Guid.Empty && reserved.Add(id))
            {
                return id;
            }
        }
    }

    private static void UpgradeViewState(JsonObject? state, Dictionary<Guid, List<Guid>> owners,
        Dictionary<Guid, Guid> sceneTracks, HashSet<Guid> groups)
    {
        if (state is null)
        {
            return;
        }
        if (state["collapsedTrackIds"] is JsonArray collapsed)
        {
            var ids = collapsed.Select(node => node?.GetValue<Guid>() ?? Guid.Empty).ToArray();
            if (ids.Length > 100000 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            {
                throw new JsonException("旧折叠轨道标识无效或重复。");
            }
            state["collapsedTrackIds"] = new JsonArray(ids.SelectMany(id =>
                {
                    var mapped = owners.GetValueOrDefault(id) ?? [];
                    if (sceneTracks.TryGetValue(id, out var sceneTrack))
                    {
                        mapped = mapped.Concat(owners[sceneTrack]).ToList();
                    }
                    return mapped.Count != 0 ? mapped : groups.Contains(id) ? [] : new List<Guid> { id };
                }).Distinct()
                .Select(id => JsonValue.Create(id) as JsonNode).ToArray());
        }
        if (state["collapsedAnimationRows"] is JsonArray rows)
        {
            var upgraded = new JsonArray();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            var originalIdentities = new HashSet<(string Scope, Guid OwnerId, AnimationProperty Property)>();
            if (rows.Count > 100000)
            {
                throw new JsonException("旧折叠动画行超过预算。");
            }
            foreach (var node in rows)
            {
                var row = node?.DeepClone().AsObject() ?? throw new JsonException("旧动画行不能为 null。");
                var id = RequiredId(row, "ownerId");
                var scope = row["scope"]?.GetValue<string>();
                var propertyName = row["property"]?.GetValue<string>();
                if (row.Count != 3 || scope is not ("SCENE_LAYER" or "SUBTITLE_TRACK") ||
                    !Enum.TryParse<AnimationProperty>(propertyName, out var property) ||
                    !AnimationPropertyMetadata.CurrentProperties.Contains(property) ||
                    !originalIdentities.Add((scope, id, property)))
                {
                    throw new JsonException("旧折叠动画行字段无效或重复。");
                }
                if (scope == "SCENE_LAYER" && groups.Contains(id))
                {
                    continue;
                }
                var mappedId = scope == "SCENE_LAYER" ? sceneTracks.GetValueOrDefault(id, id) : id;
                row["scope"] = "TRACK";
                foreach (var trackId in owners.GetValueOrDefault(mappedId) ?? [mappedId])
                {
                    var mapped = row.DeepClone().AsObject();
                    mapped["ownerId"] = trackId;
                    if (identities.Add(mapped.ToJsonString()))
                    {
                        upgraded.Add(mapped);
                    }
                }
            }
            state["collapsedAnimationRows"] = upgraded;
        }
    }
}
