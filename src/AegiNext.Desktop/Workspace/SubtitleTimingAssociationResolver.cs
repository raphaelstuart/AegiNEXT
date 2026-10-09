using AegiNext.Core.Presets;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal static class SubtitleTimingAssociationResolver
{
    private const string LEGACY_DEFAULT_STYLE_NAME = "Default";

    internal static IReadOnlyDictionary<Guid, SubtitleStylePreset> Resolve(ProjectDocument document,
        IReadOnlySet<Guid> subtitleIds, IReadOnlyList<SubtitleStylePreset> presets)
    {
        var byId = presets.ToDictionary(preset => preset.Id);
        var byName = presets.ToDictionary(preset => preset.Name, StringComparer.Ordinal);
        var hasDefaultPreset = presets.Any(preset =>
            string.Equals(preset.Name, LEGACY_DEFAULT_STYLE_NAME, StringComparison.OrdinalIgnoreCase));
        var tracks = document.Tracks.ToDictionary(track => track.Id);
        var clips = new ProjectClipIndex(document);
        var result = new Dictionary<Guid, SubtitleStylePreset>();
        foreach (var line in document.Subtitles.Where(line => subtitleIds.Contains(line.Id)))
        {
            SubtitleStylePreset? preset;
            if (line.StylePresetId is { } id)
            {
                byId.TryGetValue(id, out preset);
            }
            else if (tracks.TryGetValue(clips.GetSubtitleTrackId(line.Id), out var track) && track.StylePresetId is { } trackPresetId &&
                (string.Equals(line.StyleName, track.StylePresetName, StringComparison.Ordinal) ||
                    MatchesLegacyTrackStyle(line, track, hasDefaultPreset)))
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

    private static bool MatchesLegacyTrackStyle(SubtitleLine line, ProjectTrack track, bool hasDefaultPreset)
    {
        return !hasDefaultPreset && line.StyleName == LEGACY_DEFAULT_STYLE_NAME && track.AutoApplyStyle &&
            line.Style == track.DefaultStyle;
    }
}
