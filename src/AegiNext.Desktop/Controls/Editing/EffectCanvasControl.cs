using System.Collections.Immutable;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AegiNext.Media.Preview;
using AegiNext.Rendering.Projects;
using SkiaSharp;
using System.Runtime.InteropServices;
using PathGeometry = AegiNext.Core.Projects.PathGeometry;

namespace AegiNext.Desktop.Controls;

/// <summary>工程坐标画布；可拖拽层位置、贝塞尔锚点及控制柄，组变换参与坐标换算。</summary>
public sealed class EffectCanvasControl : Control, IDisposable
{
    private ProjectDocument document = new();
    private readonly VideoFrameSurface sceneSurface = new();
    private ProjectSceneRenderer? renderer;
    private string directory = Path.GetTempPath();
    private SdrVideoFrame? video;
    private SdrVideoFrame? compositeFrame;
    private ProjectDocument? compositeDocument;
    private bool editingPose;
    private ProjectDocument? renderedDocument;
    private MediaTime renderedPosition;
    private SdrVideoFrame? renderedVideo;
    private PixelSize renderedSize;
    private Point basePosition;
    private Point pivot;
    private Point[] corners = [];
    private SKRect localBounds;
    private readonly HashSet<(Type ErrorType, string Message)> reportedFailures = [];
    private bool disposed;
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
    private bool isRendering;
    private bool renderingFaulted;
    private bool renderingFailedInPass;
    private bool gestureCancellationPending;

    public event EventHandler<CanvasLayerEditEventArgs>? LayerEdited;
    public event EventHandler<CanvasGestureStartingEventArgs>? GestureStarting;
    public event EventHandler? GestureCancelled;
    public event EventHandler? RenderingRecovered;
    public event EventHandler<CanvasRenderingFailedEventArgs>? RenderingFailed;

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

    internal void SetScene(ProjectDocument value, ProjectLayer? layer, MediaTime time, string? assetDirectory = null, bool editorPose = false)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var directoryChanged = false;
        if (assetDirectory is { } nextDirectory && directory != nextDirectory)
        {
            renderer?.Dispose();
            renderer = null;
            renderedDocument = null;
            directory = nextDirectory;
            reportedFailures.Clear();
            directoryChanged = true;
        }

        if (!directoryChanged && ReferenceEquals(document, value) && ReferenceEquals(selected, layer) && position == time && editingPose == editorPose)
        {
            return;
        }

        if (!ReferenceEquals(document, value) || selected?.Id != layer?.Id)
        {
            CancelDrag();
        }

        if (!ReferenceEquals(document, value) || position != time)
        {
            reportedFailures.Clear();
        }

        if (!ReferenceEquals(document, value))
        {
            document = value;
            renderedDocument = null;
        }

        selected = layer;
        position = time;
        editingPose = editorPose;

        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (disposed)
        {
            return;
        }

        isRendering = true;
        renderingFailedInPass = false;
        try
        {
            RenderScene(context);
        }
        finally
        {
            isRendering = false;
            if (renderingFaulted && !renderingFailedInPass)
            {
                renderingFaulted = false;
                var recoveredDocument = document;
                var recoveredPosition = position;
                Dispatcher.UIThread.Post(() =>
                {
                    if (!disposed && !renderingFaulted && ReferenceEquals(document, recoveredDocument) && position == recoveredPosition)
                    {
                        RenderingRecovered?.Invoke(this, EventArgs.Empty);
                    }
                }, DispatcherPriority.Normal);
            }
        }
    }

    private void RenderScene(DrawingContext context)
    {
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#11151C")), null, new Rect(Bounds.Size));
        var board = ProjectRectangle;
        context.DrawRectangle(Brushes.Black, new Pen(new SolidColorBrush(Color.Parse("#66758A"))), board);
        if (board.Width <= 0 || board.Height <= 0)
        {
            return;
        }

        var sceneDocument = EditorDocument();
        if (draft is { } layerDraft)
        {
            sceneDocument = ReplaceLayer(sceneDocument, PrepareRenderedDraft(layerDraft));
            if (editingPose && selected is not null && position == selected.End)
            {
                sceneDocument = sceneDocument with { Layers = ExtendEditorEndpoint(sceneDocument.Layers, selected.Id) };
            }
        }
        PresentScene(sceneDocument, board);
        if (sceneSurface.Bitmap is { } bitmap)
        {
            context.DrawImage(bitmap, new Rect(bitmap.Size), board);
        }

        if (selected is null || !RefreshGeometry(sceneDocument))
        {
            return;
        }

        var layer = draft ?? selected;
        var fit = Fit();
        var pen = new Pen(Brushes.DeepSkyBlue, 1.5);
        var origin = pivot * fit;
        for (var index = 0; index < corners.Length; index++)
        {
            context.DrawLine(pen, corners[index] * fit, corners[(index + 1) % corners.Length] * fit);
        }

        DrawSubtitleAnchor(context, sceneDocument, layer, fit, origin);

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
            : PathOrigin(layer) * parentMatrix * fit;
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
        if (selected is null || gestureCancellationPending || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!RefreshGeometry(EditorDocument()))
        {
            return;
        }

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
                : PathOrigin(selected) * parentMatrix * fit;
            var points = Points(path);
            handle = Array.FindIndex(points, value => DistanceSquared(ToPoint(value) * matrix, point) <= 100);
            if (handle < 0)
            {
                return;
            }
        }
        else
        {
            var origin = pivot * fit;
            if (DistanceSquared(origin, point) > 484 && !ContainsLayer(point, fit))
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
        CancelDrag(false);
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
        if (gestureCancellationPending || disposed)
        {
            return false;
        }

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

        var starting = new CanvasGestureStartingEventArgs();
        GestureStarting?.Invoke(this, starting);
        if (starting.Cancel)
        {
            return false;
        }
        handle = handleIndex;
        draft = selected;
        dragStart = point;
        dragging = true;
        return true;
    }

    internal void CancelGesture() => CancelDrag();

    private void CancelDrag(bool notifyCancellation = true)
    {
        var cancelled = dragging;
        dragging = false;
        draft = null;
        handle = -1;
        if (cancelled && notifyCancellation)
        {
            GestureCancelled?.Invoke(this, EventArgs.Empty);
        }
        var pointer = capturedPointer;
        capturedPointer = null;
        if (isRendering)
        {
            if (!gestureCancellationPending)
            {
                gestureCancellationPending = true;
                Dispatcher.UIThread.Post(() => CompleteGestureCancellation(pointer), DispatcherPriority.Normal);
            }
        }
        else if (gestureCancellationPending && pointer is null)
        {
            if (!disposed)
            {
                InvalidateVisual();
            }
        }
        else
        {
            CompleteGestureCancellation(pointer);
        }
    }

    private void CompleteGestureCancellation(IPointer? pointer)
    {
        pointer?.Capture(null);
        gestureCancellationPending = false;
        if (!disposed)
        {
            InvalidateVisual();
        }
    }

    internal Rect ProjectRectangle
    {
        get
        {
            var available = new Size(Math.Max(0, Bounds.Width), Math.Max(0, Bounds.Height));
            var scale = Math.Min(available.Width / document.Width, available.Height / document.Height);
            return new((Bounds.Width - document.Width * scale) / 2, (Bounds.Height - document.Height * scale) / 2,
                document.Width * scale, document.Height * scale);
        }
    }

    /// <summary>复用播放会话合成帧；原始帧仅供暂存手势或编辑端点重绘。</summary>
    public void PresentComposite(SdrVideoFrame frame, SdrVideoFrame background)
    {
        compositeFrame = frame;
        compositeDocument = document;
        PresentVideo(background);
    }

    private ProjectDocument EditorDocument()
    {
        if (!editingPose || selected is null || position != selected.End)
        {
            return document;
        }
        return document with { Layers = ExtendEditorEndpoint(document.Layers, selected.Id) };
    }

    private ImmutableArray<ProjectLayer> ExtendEditorEndpoint(ImmutableArray<ProjectLayer> layers, Guid id)
    {
        return layers.Select(layer =>
        {
            var children = ExtendEditorEndpoint(layer.Children, id);
            var contains = layer.Id == id || children.Any(child => Contains(child, id));
            return layer with
            {
                Children = children,
                End = contains && layer.End == position ? layer.End + new MediaTime(1, 1000000) : layer.End
            };
        }).ToImmutableArray();
    }

    private static bool Contains(ProjectLayer layer, Guid id) => layer.Id == id || layer.Children.Any(child => Contains(child, id));

    private ProjectLayer PrepareRenderedDraft(ProjectLayer layerDraft)
    {
        if (selected is null || EditMode != CanvasEditMode.POSITION)
        {
            return layerDraft;
        }
        var local = position - selected.Start + selected.AnimationOffset;
        double Value(AnimationProperty property, double fallback)
        {
            var track = selected.Tracks.FirstOrDefault(track => track.Property == property);
            return track is null ? fallback : SceneEvaluator.EvaluateTrack(track, local);
        }
        return layerDraft with
        {
            Transform = layerDraft.Transform with
            {
                X = Value(AnimationProperty.POSITION_X, selected.Transform.X) + layerDraft.Transform.X - selected.Transform.X,
                Y = Value(AnimationProperty.POSITION_Y, selected.Transform.Y) + layerDraft.Transform.Y - selected.Transform.Y
            },
            Tracks = layerDraft.Tracks.Where(track => track.Property is not (AnimationProperty.POSITION_X or AnimationProperty.POSITION_Y)).ToImmutableArray()
        };
    }

    internal bool HasVideo => video is not null;
    internal bool HasPresentation => sceneSurface.Bitmap is not null;

    /// <summary>接收同一播放会话提供的原始 SDR 帧；同步复制到呈现资源，无新增解码器。</summary>
    public void PresentVideo(SdrVideoFrame frame)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        video = frame;
        renderedDocument = null;
        InvalidateVisual();
    }

    /// <summary>清除视频背景和当前呈现资源，工程底板随后重新绘制。</summary>
    public void ClearVideo()
    {
        video = null;
        compositeFrame = null;
        compositeDocument = null;
        renderedVideo = null;
        renderedDocument = null;
        sceneSurface.Clear();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            CancelDrag();
            video = null;
            compositeFrame = null;
            compositeDocument = null;
            renderedVideo = null;
            renderedDocument = null;
            sceneSurface.Dispose();
            reportedFailures.Clear();
            renderer?.Dispose();
            renderer = null;
            LayerEdited = null;
            GestureStarting = null;
            GestureCancelled = null;
            RenderingFailed = null;
            RenderingRecovered = null;
        }
    }

    private Matrix PathOrigin(ProjectLayer layer)
    {
        var local = position - layer.Start + layer.AnimationOffset;
        double Value(AnimationProperty property, double fallback)
        {
            var track = layer.Tracks.FirstOrDefault(track => track.Property == property);
            return track is null ? fallback : SceneEvaluator.EvaluateTrack(track, local);
        }
        return Matrix.CreateTranslation(basePosition.X + Value(AnimationProperty.POSITION_X, layer.Transform.X),
            basePosition.Y + Value(AnimationProperty.POSITION_Y, layer.Transform.Y));
    }

    private Matrix Fit()
    {
        var board = ProjectRectangle;
        return Matrix.CreateScale(board.Width / document.Width, board.Height / document.Height) *
               Matrix.CreateTranslation(board.X, board.Y);
    }

    private void DrawSubtitleAnchor(DrawingContext context, ProjectDocument sceneDocument, ProjectLayer layer, Matrix fit, Point origin)
    {
        if (layer.SubtitleId is not { } subtitleId || sceneDocument.Subtitles.FirstOrDefault(cue => cue.Id == subtitleId) is not { } cue)
        {
            return;
        }

        var placement = cue.Style.Position ?? SubtitlePosition.FromAlignment(cue.Style.Alignment, cue.Style.Margin);
        var anchor = new Point(placement.Anchor.X * sceneDocument.Width, placement.Anchor.Y * sceneDocument.Height) * parentMatrix * fit;
        var markerPen = new Pen(Brushes.Gold, 1.5);
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#80FFD700")), 1), anchor, origin);
        context.DrawEllipse(null, markerPen, anchor, 4.5, 4.5);
        context.DrawLine(markerPen, anchor - new Vector(7, 0), anchor + new Vector(7, 0));
        context.DrawLine(markerPen, anchor - new Vector(0, 7), anchor + new Vector(0, 7));
    }

    private bool RefreshGeometry(ProjectDocument sceneDocument)
    {
        if (selected is null || GetGeometry(sceneDocument, selected.Id) is not { } geometry)
        {
            corners = [];
            localBounds = SKRect.Empty;
            return false;
        }

        parentMatrix = ToMatrix(geometry.ParentToWorld);
        layerMatrix = ToMatrix(geometry.LocalToWorld);
        basePosition = new(geometry.BasePosition.X, geometry.BasePosition.Y);
        pivot = new(geometry.WorldPivot.X, geometry.WorldPivot.Y);
        corners = geometry.WorldCorners.Select(value => new Point(value.X, value.Y)).ToArray();
        localBounds = geometry.LocalBounds;
        return true;
    }

    private ProjectLayerGeometry? GetGeometry(ProjectDocument sceneDocument, Guid layerId)
    {
        try
        {
            renderer ??= new(new DirectoryProjectAssetResolver(directory));
            return renderer.GetLayerGeometry(sceneDocument, position, layerId);
        }
        catch (Exception error) when (IsRenderingFailure(error))
        {
            ReportRenderingFailure(error);
            return null;
        }
    }

    private bool ContainsLayer(Point point, Matrix fit)
    {
        if (!layerMatrix.TryInvert(out _) || !(layerMatrix * fit).TryInvert(out var inverse))
        {
            return false;
        }

        var local = point * inverse;
        return localBounds.Contains((float)local.X, (float)local.Y);
    }

    private void PresentScene(ProjectDocument sceneDocument, Rect board)
    {
        if (draft is null && !editingPose && ReferenceEquals(compositeDocument, sceneDocument) && compositeFrame is { } presentedFrame)
        {
            if (!ReferenceEquals(renderedVideo, presentedFrame))
            {
                sceneSurface.Present(presentedFrame);
                renderedVideo = presentedFrame;
            }
            return;
        }
        var previewScale = Math.Min(1, 1024d / Math.Max(board.Width, board.Height));
        var size = new PixelSize(Math.Max(1, (int)Math.Round(board.Width * previewScale)),
            Math.Max(1, (int)Math.Round(board.Height * previewScale)));
        if (ReferenceEquals(renderedDocument, sceneDocument) && renderedPosition == position &&
            ReferenceEquals(renderedVideo, video) && renderedSize == size)
        {
            return;
        }

        using var srgb = SKColorSpace.CreateSrgb();
        using var background = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb));
        using (var canvas = new SKCanvas(background))
        {
            canvas.Clear(SKColors.Black);
            if (video is { } frame)
            {
                using var image = SKImage.FromPixelCopy(new(frame.Width, frame.Height, SKColorType.Bgra8888, SKAlphaType.Opaque, srgb),
                    frame.Pixels.Span, checked(frame.Width * 4));
                var scale = Math.Min(size.Width / (double)frame.Width, size.Height / (double)frame.Height);
                var width = (float)(frame.Width * scale);
                var height = (float)(frame.Height * scale);
                var x = (size.Width - width) / 2;
                var y = (size.Height - height) / 2;
                canvas.DrawImage(image, new SKRect(x, y, x + width, y + height), new SKSamplingOptions(SKFilterMode.Linear));
            }
        }

        var pixels = new byte[checked(size.Width * size.Height * 4)];
        for (var row = 0; row < size.Height; row++)
        {
            Marshal.Copy(background.GetPixels() + row * background.RowBytes, pixels, row * size.Width * 4, size.Width * 4);
        }

        var composite = pixels;
        try
        {
            renderer ??= new(new DirectoryProjectAssetResolver(directory));
            composite = renderer.ComposePreview(sceneDocument, position, pixels, size.Width, size.Height, size.Width * 4);
        }
        catch (Exception error) when (IsRenderingFailure(error))
        {
            ReportRenderingFailure(error);
        }

        sceneSurface.Present(new(size.Width, size.Height, composite));
        renderedDocument = sceneDocument;
        renderedPosition = position;
        renderedVideo = video;
        renderedSize = size;
    }

    private void ReportRenderingFailure(Exception error)
    {
        renderingFaulted = true;
        renderingFailedInPass = true;
        if (HasActiveDrag)
        {
            CancelDrag();
        }

        if (reportedFailures.Add((error.GetType(), error.Message)))
        {
            var failedDocument = document;
            var failedPosition = position;
            var failure = new CanvasRenderingFailedEventArgs(document.Id, position, error);
            if (isRendering)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (!disposed && ReferenceEquals(document, failedDocument) && position == failedPosition)
                    {
                        RenderingFailed?.Invoke(this, failure);
                    }
                }, DispatcherPriority.Normal);
            }
            else
            {
                RenderingFailed?.Invoke(this, failure);
            }
        }
    }

    private static bool IsRenderingFailure(Exception error) => error is InvalidDataException or IOException or UnauthorizedAccessException or
        InvalidOperationException or NotSupportedException or ArgumentException;

    private static ProjectDocument ReplaceLayer(ProjectDocument source, ProjectLayer replacement)
    {
        return source with { Layers = ReplaceLayers(source.Layers, replacement) };
    }

    private static ImmutableArray<ProjectLayer> ReplaceLayers(ImmutableArray<ProjectLayer> layers, ProjectLayer replacement)
    {
        return layers.Select(layer => layer.Id == replacement.Id ? replacement :
            layer.Children.IsEmpty ? layer : layer with { Children = ReplaceLayers(layer.Children, replacement) }).ToImmutableArray();
    }

    private static Matrix ToMatrix(SKMatrix value) => new(value.ScaleX, value.SkewY, value.SkewX, value.ScaleY, value.TransX, value.TransY);

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
