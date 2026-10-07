using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal static class SubtitleTimingAssociationResolver
{
    internal static IReadOnlyDictionary<Guid, SubtitleStylePreset> Resolve(ProjectDocument document,
        IReadOnlySet<Guid> subtitleIds, IReadOnlyList<SubtitleStylePreset> presets)
    {
        var byId = presets.ToDictionary(preset => preset.Id);
        var byName = presets.ToDictionary(preset => preset.Name, StringComparer.Ordinal);
        var tracks = document.SubtitleTracks.ToDictionary(track => track.Id);
        var result = new Dictionary<Guid, SubtitleStylePreset>();
        foreach (var line in document.Subtitles.Where(line => subtitleIds.Contains(line.Id)))
        {
            SubtitleStylePreset? preset = null;
            if (line.StylePresetId is { } id)
            {
                byId.TryGetValue(id, out preset);
            }
            else if (tracks.TryGetValue(line.TrackId, out var track) && track.StylePresetId is { } trackPresetId &&
                string.Equals(line.StyleName, track.StylePresetName, StringComparison.Ordinal))
            {
                byId.TryGetValue(trackPresetId, out preset);
            }
            else
            {
                byName.TryGetValue(line.StyleName, out preset);
            }

            if (preset?.TimingPostProcessor is not null)
            {
                result.Add(line.Id, preset);
            }
        }

        return result;
    }
}
