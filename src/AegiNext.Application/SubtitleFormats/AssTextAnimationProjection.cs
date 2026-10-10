using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.SubtitleFormats;

internal static class AssTextAnimationProjection
{
    internal static (SubtitleLine Line, ImmutableArray<AnimationTrack>? Tracks) Restore(SubtitleLine original,
        AssTextEditResult baseline, AssTextEditResult parsed, SubtitleLine restored, ProjectLayer? layer)
    {
        var editMap = SubtitleTextEditMap.Between(original.Text, restored.Text);
        var originalRanges = SubtitleAnimationRangeEditing.Remap(original.AnimationRanges, editMap);
        var expected = SubtitleAnimationRangeEditing.Remap(baseline.Line.AnimationRanges,
            SubtitleTextEditMap.Between(baseline.Line.Text, restored.Text));
        var expectedLine = baseline.Line with { AnimationRanges = expected };
        if (Equivalent(expectedLine, baseline.NumericTracks, parsed.Line, parsed.NumericTracks))
        {
            return (restored with { AnimationRanges = originalRanges }, null);
        }
        var identities = new Dictionary<Guid, Guid>();
        var ranges = parsed.Line.AnimationRanges.Select(range =>
        {
            var previous = originalRanges.FirstOrDefault(candidate => candidate.Utf16Start == range.Utf16Start &&
                candidate.Utf16Length == range.Utf16Length);
            var id = previous?.Id ?? range.Id;
            identities.Add(range.Id, id);
            return range with { Id = id, Pivot = previous?.Pivot ?? range.Pivot };
        }).ToImmutableArray();
        ranges = ranges.AddRange(originalRanges.Where(range => !ranges.Any(candidate => candidate.Id == range.Id) &&
            !expected.Any(candidate => candidate.Utf16Start == range.Utf16Start && candidate.Utf16Length == range.Utf16Length)));
        var tracks = parsed.NumericTracks.Select(track => track.Target.TextRangeId is { } id ? track with
        {
            Target = track.Target with { TextRangeId = identities[id] }
        } : track).ToImmutableArray();
        var preserved = layer?.Tracks.Where(track => !IsTextTrack(track) || !Representable(track)).ToImmutableArray() ?? [];
        return (restored with { AnimationRanges = ranges }, preserved.AddRange(tracks));
    }

    internal static bool IsTextTrack(AnimationTrack track) => track.Target.TextRangeId is not null ||
        track.Property is AnimationProperty.FONT_SIZE or AnimationProperty.LETTER_SPACING or AnimationProperty.FILL or
        AnimationProperty.STROKE or AnimationProperty.STROKE_WIDTH or AnimationProperty.FILL_BLUR or AnimationProperty.STROKE_BLUR or
        AnimationProperty.SHADOW_OFFSET or AnimationProperty.SHADOW_BLUR or AnimationProperty.SHADOW_COLOR;

    private static bool Representable(AnimationTrack track) => track.Property != AnimationProperty.SHADOW_BLUR &&
        (track.Target.State == SubtitleAnimationState.NORMAL || track.Property == AnimationProperty.FILL);

    private static bool Equivalent(SubtitleLine firstLine, ImmutableArray<AnimationTrack> first,
        SubtitleLine secondLine, ImmutableArray<AnimationTrack> second)
    {
        var firstGeometry = firstLine.AnimationRanges.Where(range => range.Scale != new ScenePoint(1, 1) || range.Rotation != 0)
            .Select(range => (range.Utf16Start, range.Utf16Length, range.Scale, range.Rotation));
        var secondGeometry = secondLine.AnimationRanges.Where(range => range.Scale != new ScenePoint(1, 1) || range.Rotation != 0)
            .Select(range => (range.Utf16Start, range.Utf16Length, range.Scale, range.Rotation));
        if (!firstGeometry.SequenceEqual(secondGeometry))
        {
            return false;
        }
        if (first.Length != second.Length)
        {
            return false;
        }
        foreach (var track in first)
        {
            var scope = Scope(firstLine, track);
            var candidate = second.FirstOrDefault(candidate => Scope(secondLine, candidate) == scope);
            if (candidate is null || track.ColorSpace != candidate.ColorSpace || track.InitialValue != candidate.InitialValue ||
                !track.Keyframes.SequenceEqual(candidate.Keyframes) || track.Transforms.Length != candidate.Transforms.Length ||
                track.Transforms.Zip(candidate.Transforms).Any(pair => pair.First with { Id = pair.Second.Id } != pair.Second))
            {
                return false;
            }
        }
        return true;
    }

    private static (AnimationProperty Property, SubtitleAnimationState State, int Start, int Length) Scope(SubtitleLine line, AnimationTrack track)
    {
        var range = line.AnimationRanges.FirstOrDefault(range => range.Id == track.Target.TextRangeId);
        return (track.Property, track.Target.State, range?.Utf16Start ?? -1, range?.Utf16Length ?? -1);
    }
}
