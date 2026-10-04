using AegiNext.Core.Editing;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal static class AnimationEditOperations
{
    internal static double Value(ProjectLayer layer, AnimationProperty property, AnimationEditTarget target, double fallback)
    {
        var track = layer.Tracks.FirstOrDefault(value => value.Property == property);
        return track is null ? fallback : SceneEvaluator.EvaluateTrack(track, target.LocalTime);
    }

    internal static ProjectDocument SetValue(ProjectDocument document, AnimationEditTarget target, AnimationProperty property, double value)
    {
        var layer = WorkbenchSession.Flatten(document.Layers).Single(value => value.Id == target.LayerId);
        var track = layer.Tracks.FirstOrDefault(value => value.Property == property);
        var range = LayerAnimationTiming.GetRange(layer);
        if ((target.IsKeyframe || track is not null) && target.LocalTime >= range.Minimum && target.LocalTime <= range.Maximum)
        {
            var existing = track?.Keyframes.FirstOrDefault(frame => frame.Time == target.LocalTime);
            return WorkspaceDraftOperations.SetKeyframe(document, layer.Id, property,
                existing is null ? new(target.LocalTime, value) : existing with { Value = value });
        }

        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, item => property switch
        {
            AnimationProperty.POSITION_X => item with { Transform = item.Transform with { X = value } },
            AnimationProperty.POSITION_Y => item with { Transform = item.Transform with { Y = value } },
            AnimationProperty.SCALE_X => item with { Transform = item.Transform with { ScaleX = value } },
            AnimationProperty.SCALE_Y => item with { Transform = item.Transform with { ScaleY = value } },
            AnimationProperty.ROTATION => item with { Transform = item.Transform with { Rotation = value } },
            AnimationProperty.OPACITY => item with { Opacity = value },
            AnimationProperty.BLUR => item with { Blur = value },
            AnimationProperty.STROKE_WIDTH => item with { StrokeWidth = value },
            _ => item
        });
    }
}
