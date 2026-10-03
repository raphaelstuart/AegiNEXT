using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using PathGeometry = AegiNext.Core.Projects.PathGeometry;

namespace AegiNext.Desktop.Controls;

/// <summary>工程坐标画布；可拖拽层位置、贝塞尔锚点及控制柄，组变换参与坐标换算。</summary>
public sealed class EffectCanvasControl : Control
{
    private ProjectDocument document = new();
    private PreparedProjectScene prepared = new(new());
    private ProjectLayer? selected;
    private ProjectLayer? draft;
    private Matrix parentMatrix = Matrix.Identity;
    private Matrix layerMatrix = Matrix.Identity;
    private Point dragStart;
    private int handle = -1;
    private bool dragging;
    private MediaTime position;
    private CanvasEditMode editMode;
    private IPointer? capturedPointer;

    public event EventHandler<CanvasLayerEditEventArgs>? LayerEdited;

    internal CanvasEditMode EditMode
    {
        get => editMode;
        set
        {
            if (editMode != value)
            {
                CancelDrag();
                editMode = value;
                InvalidateVisual();
            }
        }
    }

    internal bool HasActiveDrag => dragging;

    internal void SetScene(ProjectDocument value, ProjectLayer? layer, MediaTime time)
    {
        if (ReferenceEquals(document, value) && ReferenceEquals(selected, layer) && position == time)
        {
            return;
        }

        if (!ReferenceEquals(document, value) || selected?.Id != layer?.Id)
        {
            CancelDrag();
        }

        if (!ReferenceEquals(document, value))
        {
            document = value;
            prepared = new(value);
        }

        selected = layer;
        position = time;
        IsHitTestVisible = layer is not null;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (selected is null)
        {
            return;
        }

        var visible = Find(SceneEvaluator.Evaluate(prepared, position), selected.Id, Matrix.Identity);
        if (visible is null)
        {
            return;
        }

        parentMatrix = visible.Value.Parent;
        layerMatrix = Transform(visible.Value.Layer.Transform) * parentMatrix;
        var layer = draft ?? selected;
        var fit = Fit();
        var pen = new Pen(Brushes.DeepSkyBlue, 1.5);
        var origin = new Point(layer.Transform.X, layer.Transform.Y) * parentMatrix * fit;
        if (EditMode == CanvasEditMode.POSITION)
        {
            context.DrawEllipse(null, pen, origin, 9, 9);
            context.DrawLine(pen, origin - new Vector(15, 0), origin + new Vector(15, 0));
            context.DrawLine(pen, origin - new Vector(0, 15), origin + new Vector(0, 15));
            return;
        }

        var path = EditMode == CanvasEditMode.MASK ? layer.Mask?.Path : layer.MotionPath?.Path;
        if (path is null)
        {
            return;
        }

        var matrix = EditMode == CanvasEditMode.MASK
            ? layerMatrix * fit
            : Matrix.CreateTranslation(layer.Transform.X, layer.Transform.Y) * parentMatrix * fit;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(ToPoint(path.Start) * matrix, false);
            foreach (var segment in path.Segments)
            {
                stream.CubicBezierTo(ToPoint(segment.Control1) * matrix, ToPoint(segment.Control2) * matrix,
                    ToPoint(segment.End) * matrix);
            }

            stream.EndFigure(path.Closed);
        }

        context.DrawGeometry(null, pen, geometry);
        var previous = path.Start;
        foreach (var segment in path.Segments)
        {
            context.DrawLine(new Pen(Brushes.Gray), ToPoint(previous) * matrix, ToPoint(segment.Control1) * matrix);
            context.DrawLine(new Pen(Brushes.Gray), ToPoint(segment.Control2) * matrix,
                ToPoint(segment.End) * matrix);
            previous = segment.End;
        }

        var points = Points(path);
        for (var index = 0; index < points.Length; index++)
        {
            context.DrawEllipse(index % 3 == 0 ? Brushes.DeepSkyBlue : Brushes.White, pen,
                ToPoint(points[index]) * matrix, 4, 4);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (selected is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        var fit = Fit();
        if (EditMode != CanvasEditMode.POSITION)
        {
            var path = EditMode == CanvasEditMode.MASK ? selected.Mask?.Path : selected.MotionPath?.Path;
            if (path is null)
            {
                return;
            }

            var matrix = EditMode == CanvasEditMode.MASK
                ? layerMatrix * fit
                : Matrix.CreateTranslation(selected.Transform.X, selected.Transform.Y) * parentMatrix * fit;
            var points = Points(path);
            handle = Array.FindIndex(points, value => DistanceSquared(ToPoint(value) * matrix, point) <= 100);
            if (handle < 0)
            {
                return;
            }
        }
        else
        {
            var origin = new Point(selected.Transform.X, selected.Transform.Y) * parentMatrix * fit;
            if (DistanceSquared(origin, point) > 484)
            {
                return;
            }
        }

        if (!BeginDrag(point, handle))
        {
            return;
        }

        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!dragging || selected is null)
        {
            return;
        }

        var fit = Fit();
        var matrix = EditMode == CanvasEditMode.MASK ? layerMatrix * fit : parentMatrix * fit;
        if (!matrix.TryInvert(out var inverse))
        {
            return;
        }

        var current = e.GetPosition(this) * inverse;
        var initial = dragStart * inverse;
        var delta = new Vector(current.X - initial.X, current.Y - initial.Y);
        if (EditMode == CanvasEditMode.POSITION)
        {
            draft = selected with
            {
                Transform = selected.Transform with
                {
                    X = selected.Transform.X + delta.X, Y = selected.Transform.Y + delta.Y
                }
            };
        }
        else
        {
            var source = EditMode == CanvasEditMode.MASK ? selected.Mask!.Path : selected.MotionPath!.Path;
            var edited = MoveHandle(source, handle, delta);
            draft = EditMode == CanvasEditMode.MASK
                ? selected with { Mask = selected.Mask! with { Path = edited } }
                : selected with { MotionPath = selected.MotionPath! with { Path = edited } };
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var result = draft;
        var commit = dragging;
        CancelDrag();
        if (commit && result is not null)
        {
            LayerEdited?.Invoke(this, new(result.Id, result.Transform, result.MotionPath, result.Mask));
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelDrag();
    }

    internal bool BeginDrag(Point point, int handleIndex = -1)
    {
        CancelDrag();
        if (selected is null)
        {
            return false;
        }

        if (EditMode != CanvasEditMode.POSITION)
        {
            var path = EditMode == CanvasEditMode.MASK ? selected.Mask?.Path : selected.MotionPath?.Path;
            if (path is null || handleIndex < 0 || handleIndex > (long)path.Segments.Length * 3)
            {
                return false;
            }
        }

        handle = handleIndex;
        draft = selected;
        dragStart = point;
        dragging = true;
        return true;
    }

    internal void CancelGesture() => CancelDrag();

    private void CancelDrag()
    {
        dragging = false;
        draft = null;
        handle = -1;
        var pointer = capturedPointer;
        capturedPointer = null;
        pointer?.Capture(null);
        InvalidateVisual();
    }

    private Matrix Fit()
    {
        var scale = Math.Min(Bounds.Width / document.Width, Bounds.Height / document.Height);
        return Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - document.Width * scale) / 2,
            (Bounds.Height - document.Height * scale) / 2);
    }

    private static (EvaluatedLayer Layer, Matrix Parent)? Find(ImmutableArray<EvaluatedLayer> layers, Guid id,
        Matrix parent)
    {
        foreach (var layer in layers)
        {
            if (layer.Source.Id == id)
            {
                return (layer, parent);
            }

            var child = Find(layer.Children, id, Transform(layer.Transform) * parent);
            if (child is not null)
            {
                return child;
            }
        }

        return null;
    }

    private static Matrix Transform(LayerTransform value)
    {
        return Matrix.CreateTranslation(-value.AnchorX, -value.AnchorY) *
               Matrix.CreateScale(value.ScaleX, value.ScaleY) *
               Matrix.CreateRotation(value.Rotation * Math.PI / 180) * Matrix.CreateTranslation(value.X, value.Y);
    }

    private static Point ToPoint(ScenePoint point)
    {
        return new(point.X, point.Y);
    }

    private static double DistanceSquared(Point first, Point second)
    {
        return Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2);
    }

    private static ScenePoint[] Points(PathGeometry path)
    {
        return new[] { path.Start }
            .Concat(path.Segments.SelectMany(segment => new[] { segment.Control1, segment.Control2, segment.End }))
            .ToArray();
    }

    internal static PathGeometry MoveHandle(PathGeometry path, int index, Vector delta)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        if (path.Segments.IsDefault || index > (long)path.Segments.Length * 3)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y))
        {
            throw new ArgumentOutOfRangeException(nameof(delta));
        }

        static ScenePoint Move(ScenePoint value, Vector offset)
        {
            var moved = new ScenePoint(value.X + offset.X, value.Y + offset.Y);
            if (!double.IsFinite(moved.X) || !double.IsFinite(moved.Y))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return moved;
        }

        if (index == 0)
        {
            return path with { Start = Move(path.Start, delta) };
        }

        var segmentIndex = (index - 1) / 3;
        var segment = path.Segments[segmentIndex];
        var changed = ((index - 1) % 3) switch
        {
            0 => segment with { Control1 = Move(segment.Control1, delta) },
            1 => segment with { Control2 = Move(segment.Control2, delta) },
            _ => segment with { End = Move(segment.End, delta) }
        };
        return path with { Segments = path.Segments.SetItem(segmentIndex, changed) };
    }
}
