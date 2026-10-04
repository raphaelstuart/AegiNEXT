using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private MediaTime? inspectorTime;
    private Guid? inspectorLayerId;

    internal SceneEditingState SceneEditing { get; } = new();

    internal AnimationEditTarget? AnimationTarget => SelectedLayer is { } layer
        ? SceneEditing.GestureTarget ?? SceneEditing.DraftTarget ?? new(layer.Id, SelectedKeyTime ?? LayerAnimationTiming.ClampTime(layer, ProjectPosition - layer.Start + layer.AnimationOffset), SelectedKeyTime is not null)
        : null;

    internal MediaTime EditingPosition => SelectedLayer is { } layer && (SceneEditing.GestureTarget?.LocalTime ?? SelectedKeyTime) is { } time
        ? layer.Start + time - layer.AnimationOffset : ProjectPosition;

    internal event EventHandler? SceneGestureCancellationRequested;

    internal void CancelSceneGesture()
    {
        CancelCanvasGesture();
        SceneGestureCancellationRequested?.Invoke(this, EventArgs.Empty);
    }

    private void FreezeDraftTarget()
    {
        if (SceneEditing.DraftTarget is not null || SelectedLayer is not { } layer)
        {
            return;
        }
        var local = SelectedKeyTime ?? (inspectorTime ?? ProjectPosition) - layer.Start + layer.AnimationOffset;
        SceneEditing.DraftTarget = new(layer.Id, LayerAnimationTiming.ClampTime(layer, local), SelectedKeyTime is not null, ActiveProperty);
        _ = RunCommandAsync(PauseForSceneEditAsync);
    }

    internal Task PauseForSceneEditAsync() => controller.Snapshot.State == AegiNext.Media.Playback.VideoPlaybackState.PLAYING
        ? controller.PauseAsync() : Task.CompletedTask;

    internal bool BeginCanvasGesture()
    {
        if (!TryCommitDrafts() || AnimationTarget is not { } target)
        {
            return false;
        }
        SceneEditing.GestureTarget = target;
        _ = RunCommandAsync(PauseForSceneEditAsync);
        return true;
    }

    internal void CancelCanvasGesture() => SceneEditing.GestureTarget = null;

    internal Task CommitCanvasAsync(CanvasLayerEditEventArgs value) => RunCommandAsync(() => EditAsync(() =>
    {
        var layer = SelectedLayer;
        var target = SceneEditing.GestureTarget ?? AnimationTarget;
        SceneEditing.GestureTarget = null;
        if (layer is null || target is null || layer.Id != value.LayerId)
        {
            return;
        }
        var document = editor.Snapshot;
        var prepared = WorkspaceDraftOperations.UpdateLayer(document, layer.Id, item => item with { MotionPath = value.Path, Mask = value.Mask });
        var transform = layer.Transform;
        foreach (var (property, changed, original) in new[]
        {
            (AnimationProperty.POSITION_X, value.Transform.X, transform.X),
            (AnimationProperty.POSITION_Y, value.Transform.Y, transform.Y),
            (AnimationProperty.SCALE_X, value.Transform.ScaleX, transform.ScaleX),
            (AnimationProperty.SCALE_Y, value.Transform.ScaleY, transform.ScaleY),
            (AnimationProperty.ROTATION, value.Transform.Rotation, transform.Rotation)
        })
        {
            if (changed != original)
            {
                var evaluated = AnimationEditOperations.Value(layer, property, target, original);
                prepared = AnimationEditOperations.SetValue(prepared, target, property, evaluated + changed - original);
            }
        }
        if (prepared != document)
        {
            editor.Apply("Edit scene in video preview", _ => prepared);
        }
    }));

    private void RefreshAnimatedInspectorAtTime()
    {
        if (effectsDirty || stylesDirty || inspectorTime == EditingPosition && inspectorLayerId == SelectedLayerId)
        {
            return;
        }
        inspectorTime = EditingPosition;
        inspectorLayerId = SelectedLayerId;
        var previous = updatingWorkbench;
        updatingWorkbench = true;
        try
        {
            RefreshInspector();
        }
        finally
        {
            updatingWorkbench = previous;
        }
    }

    private void RefreshEditingPreview()
    {
        ViewModel.Preview.Scene = new(DocumentSnapshot, SelectedLayer, EditingPosition, SceneEditing.Mode,
            ProjectDirectory, SelectedKeyTime is not null || SceneEditing.GestureTarget is not null, playback.IsInteractive);
        Volatile.Write(ref previewState, new(DocumentSnapshot, ProjectDirectory, ProjectPosition, playback.IsInteractive));
    }

    private double InspectorValue(ProjectLayer layer, AnimationProperty property, double fallback) =>
        AnimationTarget is { } target ? AnimationEditOperations.Value(layer, property, target, fallback) : fallback;

    private SceneColor InspectorColor(ProjectLayer layer, SceneColor color, bool stroke)
    {
        var first = stroke ? AnimationProperty.STROKE_RED : AnimationProperty.FILL_RED;
        return new(InspectorValue(layer, first, color.Red), InspectorValue(layer, first + 1, color.Green),
            InspectorValue(layer, first + 2, color.Blue), InspectorValue(layer, first + 3, color.Alpha));
    }

    private SceneColor PrepareColor(ref ProjectDocument document, ProjectLayer layer, Avalonia.Media.Color draft, SceneColor original, bool stroke)
    {
        var displayed = InspectorColor(layer, original, stroke);
        var requested = PreserveColor(draft, displayed);
        var result = original;
        var properties = stroke
            ? new[] { AnimationProperty.STROKE_RED, AnimationProperty.STROKE_GREEN, AnimationProperty.STROKE_BLUE, AnimationProperty.STROKE_ALPHA }
            : new[] { AnimationProperty.FILL_RED, AnimationProperty.FILL_GREEN, AnimationProperty.FILL_BLUE, AnimationProperty.FILL_ALPHA };
        var values = new[] { requested.Red, requested.Green, requested.Blue, requested.Alpha };
        var previous = new[] { displayed.Red, displayed.Green, displayed.Blue, displayed.Alpha };
        for (var index = 0; index < properties.Length; index++)
        {
            if (values[index] == previous[index])
            {
                continue;
            }
            if (AnimationTarget is { } target && (target.IsKeyframe || layer.Tracks.Any(track => track.Property == properties[index])))
            {
                document = AnimationEditOperations.SetValue(document, target, properties[index], values[index]);
            }
            else
            {
                result = index switch
                {
                    0 => result with { Red = values[index] },
                    1 => result with { Green = values[index] },
                    2 => result with { Blue = values[index] },
                    _ => result with { Alpha = values[index] }
                };
            }
        }
        return result;
    }

    private double PrepareStrokeWidth(ref ProjectDocument document, ProjectLayer layer, double baseValue, string text)
    {
        var original = InspectorValue(layer, AnimationProperty.STROKE_WIDTH, baseValue);
        var requested = ReadNumber(text, original, "StrokeWidth", "StrokeWidthInput");
        if (requested != original && AnimationTarget is { } target && (target.IsKeyframe || layer.Tracks.Any(track => track.Property == AnimationProperty.STROKE_WIDTH)))
        {
            document = AnimationEditOperations.SetValue(document, target, AnimationProperty.STROKE_WIDTH, requested);
            return baseValue;
        }
        return requested == original ? baseValue : requested;
    }

    private void RefreshEditingTargetLabel()
    {
        ViewModel.Effects.EditTargetLabel = AnimationTarget is { } target
            ? $"{WorkbenchText.Get(target.IsKeyframe ? "EditKeyframeTarget" : "EditPlayheadTarget")} {TimelineTimeText.Format(target.LocalTime)}"
            : WorkbenchText.Get("NoSelection");
    }
}
