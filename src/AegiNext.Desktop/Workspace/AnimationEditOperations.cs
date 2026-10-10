using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal static class AnimationEditOperations
{
    internal static double Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, double fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == ResolveTarget(target, property));
        return track is null ? fallback : SceneEvaluator.EvaluateScalarTrack(track, target.LocalTime);
    }

    internal static ScenePoint Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, ScenePoint fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == ResolveTarget(target, property));
        return track is null ? fallback : SceneEvaluator.EvaluateVectorTrack(track, target.LocalTime);
    }

    internal static SceneColor Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, SceneColor fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == ResolveTarget(target, property));
        return track is null ? fallback : SceneEvaluator.EvaluateColorTrack(track, target.LocalTime);
    }

    internal static ProjectDocument SetValue(ProjectDocument document, AnimationEditTarget target, AnimationProperty property, AnimationValue value) =>
        SetValue(document, target, ResolveTarget(target, property), value);

    internal static ProjectDocument SetValue(ProjectDocument document, AnimationEditTarget target, AnimationTrackTarget animationTarget, AnimationValue value)
    {
        var property = animationTarget.Property;
        var layer = document.Layers.Single(value => value.Id == target.LayerId);
        var track = layer.Tracks.FirstOrDefault(value => value.Target == animationTarget);
        if (track is not null && !track.Transforms.IsEmpty)
        {
            throw new InvalidOperationException("Ordered transforms must be edited by their operation identity.");
        }
        var range = LayerAnimationTiming.GetRange(layer);
        if ((target.IsKeyframe || track is not null) && target.LocalTime >= range.Minimum && target.LocalTime <= range.Maximum)
        {
            var existing = track?.Keyframes.FirstOrDefault(frame => frame.Time == target.LocalTime);
            return WorkspaceDraftOperations.SetKeyframe(document, layer.Id, animationTarget,
                existing is null ? new(target.LocalTime, value) : existing with { Value = value });
        }

        return SubtitleAnimationEditing.SetBaseValue(document, layer.Id, animationTarget, value);
    }

    private static AnimationTrackTarget ResolveTarget(AnimationEditTarget target, AnimationProperty property) =>
        target.Target is { } identity && !AnimationPropertyMetadata.IsMaskProperty(property)
            ? identity with { Property = property, NodeId = null,
                State = AnimationPropertyMetadata.IsSubtitleVisualProperty(property) ? identity.State : SubtitleAnimationState.NORMAL } : new(property);

}
