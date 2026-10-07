using AegiNext.Core.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

public sealed partial class EffectCanvasControl
{
    private const double PATH_HIT_RADIUS_SQUARED = 100;
    private const int MAX_PATH_SEGMENTS = 10000;
    private PathCanvasTopologyEdit? pathTopologyEdit;

    private bool PathTopologyPointerPressed(PointerPressedEventArgs e, Point point, Matrix matrix)
    {
        var action = BezierEditingModifiers.GetAction(e.KeyModifiers);
        if (EditMode != CanvasEditMode.PATH || action == BezierEditAction.NONE)
        {
            return false;
        }
        e.Handled = true;
        var candidate = HitPathTopology(action, point, matrix);
        if (candidate is null)
        {
            return true;
        }

        CancelDrag();
        var starting = new CanvasGestureStartingEventArgs();
        GestureStarting?.Invoke(this, starting);
        if (starting.Cancel)
        {
            return true;
        }
        if (!IsCurrentPathTopology(candidate))
        {
            GestureCancelled?.Invoke(this, EventArgs.Empty);
            return true;
        }
        Focusable = true;
        Focus();
        if (!IsCurrentPathTopology(candidate))
        {
            GestureCancelled?.Invoke(this, EventArgs.Empty);
            return true;
        }
        pathTopologyEdit = candidate;
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        if (!IsCurrentPathTopology(candidate) || !ReferenceEquals(pathTopologyEdit, candidate) || !ReferenceEquals(e.Pointer.Captured, this))
        {
            CancelDrag();
        }
        return true;
    }

    private PathCanvasTopologyEdit? HitPathTopology(BezierEditAction action, Point point, Matrix matrix)
    {
        if (selected is not { MotionPath: { } motionPath } layer || TopLevel.GetTopLevel(this) is not { } host ||
            !new Rect(Bounds.Size).Contains(point))
        {
            return null;
        }
        var path = motionPath.Path;
        var distance = PATH_HIT_RADIUS_SQUARED;
        PathCanvasTopologyEdit? closest = null;
        if (action == BezierEditAction.DELETE)
        {
            if (path.Segments.Length <= 1)
            {
                return null;
            }
            for (var index = 0; index <= path.Segments.Length; index++)
            {
                var anchor = index == 0 ? path.Start : path.Segments[index - 1].End;
                var projected = ToPoint(anchor) * matrix;
                var candidateDistance = DistanceSquared(projected, point);
                if (candidateDistance <= distance)
                {
                    distance = candidateDistance;
                    closest = new(document, layer, position, editingPose && position == layer.End, host, action, index, 0, projected);
                }
            }
            return closest;
        }
        if (path.Segments.Length >= MAX_PATH_SEGMENTS)
        {
            return null;
        }
        for (var index = 0; index < path.Segments.Length; index++)
        {
            var segment = path.Segments[index];
            var start = ToPoint(index == 0 ? path.Start : path.Segments[index - 1].End) * matrix;
            var end = ToPoint(segment.End) * matrix;
            var hit = CubicBezierHitTest.FindNearest(start, ToPoint(segment.Control1) * matrix,
                ToPoint(segment.Control2) * matrix, end, point, Math.Sqrt(distance));
            if (hit is { } result && result.Progress > 0 && result.Progress < 1 &&
                DistanceSquared(result.Position, start) > 0.25 && DistanceSquared(result.Position, end) > 0.25)
            {
                distance = result.DistanceSquared;
                closest = new(document, layer, position, editingPose && position == layer.End, host, action, index, result.Progress, result.Position);
            }
        }
        return closest;
    }

    private bool IsCurrentPathTopology(PathCanvasTopologyEdit edit) =>
        !disposed && EditMode == CanvasEditMode.PATH && ReferenceEquals(document, edit.Document) &&
        ReferenceEquals(selected, edit.Layer) && position == edit.Time && (editingPose && position == edit.Layer.End) == edit.EditingEndpoint &&
        ReferenceEquals(TopLevel.GetTopLevel(this), edit.Host);

    private void PathTopologyPointerReleased(PointerReleasedEventArgs e)
    {
        if (pathTopologyEdit is not { } edit || e.InitialPressMouseButton != MouseButton.Left ||
            !ReferenceEquals(capturedPointer, e.Pointer))
        {
            return;
        }
        e.Handled = true;
        if (!IsCurrentPathTopology(edit) || DistanceSquared(e.GetPosition(this), edit.Position) > PATH_HIT_RADIUS_SQUARED)
        {
            CancelDrag();
            return;
        }

        var motionPath = edit.Layer.MotionPath!;
        var edited = edit.Action == BezierEditAction.INSERT
            ? PathOperations.SplitSegment(motionPath.Path, edit.Index, edit.Progress)
            : PathOperations.RemovePoint(motionPath.Path, edit.Index);
        CancelDrag(false);
        if (!IsCurrentPathTopology(edit))
        {
            GestureCancelled?.Invoke(this, EventArgs.Empty);
            return;
        }
        LayerEdited?.Invoke(this, new(edit.Layer.Id, edit.Layer.Transform, motionPath with { Path = edited }));
        InvalidateVisual();
    }
}
