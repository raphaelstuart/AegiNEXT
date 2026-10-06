using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal static class AnimationEditOperations
{
    internal static double Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, double fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == new AnimationTrackTarget(property));
        return track is null ? fallback : SceneEvaluator.EvaluateScalarTrack(track, target.LocalTime);
    }

    internal static ScenePoint Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, ScenePoint fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == new AnimationTrackTarget(property));
        return track is null ? fallback : SceneEvaluator.EvaluateVectorTrack(track, target.LocalTime);
    }

    internal static SceneColor Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, SceneColor fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Target == new AnimationTrackTarget(property));
        return track is null ? fallback : SceneEvaluator.EvaluateColorTrack(track, target.LocalTime);
    }

    internal static ProjectDocument SetValue(ProjectDocument document, AnimationEditTarget target, AnimationProperty property, AnimationValue value) =>
        SetValue(document, target, new AnimationTrackTarget(property), value);

    internal static ProjectDocument SetValue(ProjectDocument document, AnimationEditTarget target, AnimationTrackTarget animationTarget, AnimationValue value)
    {
        var property = animationTarget.Property;
        var layer = WorkbenchSession.Flatten(document.Layers).Single(value => value.Id == target.LayerId);
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

        if (AnimationPropertyMetadata.IsMaskProperty(property))
        {
            return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, item => item with
            {
                Mask = ClipMaskAnimation.SetBaseValue(item.Mask ?? throw new InvalidOperationException("Clip mask required."), animationTarget, value)
            });
        }

        if (property is AnimationProperty.FILL or AnimationProperty.STROKE && layer.SubtitleId is { } subtitleId)
        {
            return WorkspaceDraftOperations.UpdateSubtitle(document, subtitleId, subtitle => subtitle with
            {
                Style = property == AnimationProperty.FILL
                    ? subtitle.Style with { Fill = value.Color }
                    : subtitle.Style with { Stroke = value.Color }
            });
        }

        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, item => property switch
        {
            AnimationProperty.FILL => item with { Fill = value.Color },
            AnimationProperty.STROKE => item with { Stroke = value.Color },
            AnimationProperty.POSITION => item with { Transform = item.Transform with { Position = value.Vector } },
            AnimationProperty.SCALE => item with { Transform = item.Transform with { Scale = value.Vector } },
            AnimationProperty.ROTATION => item with { Transform = item.Transform with { Rotation = value.Scalar } },
            AnimationProperty.OPACITY => item with { Opacity = value.Scalar },
            AnimationProperty.BLUR => item with { Blur = value.Scalar },
            AnimationProperty.STROKE_WIDTH => item with { StrokeWidth = value.Scalar },
            _ => item
        });
    }
}
