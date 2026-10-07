using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;

namespace AegiNext.Desktop.Controls;

/// <summary>完整工程概览；拖动视窗平移，拖动两边缩放，始终不发出播放定位。</summary>
public sealed class TimelineOverviewControl : Control, IDisposable
{
    private readonly TimelineDrawingCache clipDrawing = new();
    private readonly Dictionary<Guid, int[]> clipsByCue = [];
    private IBrush surfaceBrush = Brushes.Transparent;
    private IBrush subtitleBrush = Brushes.Transparent;
    private IBrush layerBrush = Brushes.Transparent;
    private IBrush viewportBrush = new SolidColorBrush(Color.Parse("#304778D8"));
    private readonly Pen viewportBorder = new(Brushes.RoyalBlue, 2);
    private readonly Pen viewportEdge = new(Brushes.RoyalBlue, 3);
    private Pen playheadPen = new(Brushes.Transparent, 2);
    private ProjectDocument document = new();
    private TimelineViewport viewport = new();
    private double duration = 1;
    private MediaTime position;
    private TimelineTimingPreview? timingPreview;
    private TimelineOverviewDragMode dragMode;
    private TimelineViewport original = new();
    private double pointerOrigin;
    private IPointer? capturedPointer;
    private (double Start, double End, int Row, bool Subtitle, Guid? CueId)[] clips = [];

    /// <summary>建立可聚焦的导航概览。</summary>
    public TimelineOverviewControl()
    {
        Focusable = true;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => RefreshDrawingPalette();
        RefreshDrawingPalette();
    }

    public event EventHandler<TimelineViewportEventArgs>? ViewportChanged;
    internal long StaticDrawingBuildCount { get; private set; }
    internal long CachedDrawingBytes => clipDrawing.AllocatedBytes;

    public MediaTime Position
    {
        get => position;
        set
        {
            if (position != value)
            {
                position = value;
                InvalidateVisual();
            }
        }
    }

    internal Rect ViewportRectangle
    {
        get
        {
            var inset = Math.Min(2, Bounds.Width / 4);
            var rightLimit = Bounds.Width - inset;
            var minimumWidth = Math.Min(2, Math.Max(0, rightLimit - inset));
            var start = Math.Clamp(viewport.StartSeconds, 0, duration);
            var end = Math.Clamp(viewport.StartSeconds + viewport.VisibleDuration, start, duration);
            var left = Math.Clamp(start / duration * Bounds.Width, inset, Math.Max(inset, rightLimit - minimumWidth));
            var right = Math.Clamp(end / duration * Bounds.Width, left + minimumWidth, rightLimit);
            return new(left, 1, Math.Max(0, right - left), Math.Max(0, Bounds.Height - 2));
        }
    }

    /// <summary>呈现同一工程和视口，保持全部导航状态来自调用方。</summary>
    public void SetScene(ProjectDocument value, TimelineViewport visible, double totalDuration, MediaTime playhead,
        TimelineTimingPreview? preview = null)
    {
        var nextDuration = Math.Max(0.001, totalDuration);
        if (ReferenceEquals(document, value) && viewport == visible && duration.Equals(nextDuration) &&
            position == playhead && timingPreview == preview)
        {
            return;
        }

        if (!ReferenceEquals(document, value))
        {
            CancelGesture();
            clipDrawing.Dispose();
            var indices = value.SubtitleTracks.Select((track, index) => (track.Id, index)).ToDictionary(item => item.Id, item => item.index);
            var cues = value.Subtitles.ToDictionary(cue => cue.Id);
            clips = Flatten(value.Layers).Select(layer => (Seconds(layer.Start), Seconds(layer.End),
                layer.SubtitleId is { } cueId ? indices[cues[cueId].TrackId] : value.SubtitleTracks.Length,
                layer.Kind == LayerKind.SUBTITLE, layer.SubtitleId)).ToArray();
            clipsByCue.Clear();
            foreach (var group in clips.Select((clip, index) => (clip.CueId, Index: index))
                         .Where(item => item.CueId.HasValue).GroupBy(item => item.CueId!.Value))
            {
                clipsByCue.Add(group.Key, group.Select(item => item.Index).ToArray());
            }
        }

        if (!duration.Equals(nextDuration) || timingPreview?.CueId != preview?.CueId)
        {
            clipDrawing.Dispose();
        }
        document = value;
        viewport = visible;
        duration = nextDuration;
        position = playhead;
        timingPreview = preview;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
        clipDrawing.Draw(context, Bounds.Size, scaling, TimelineDrawingCache.CanCache(Bounds.Size, scaling, 1), drawing =>
        {
            StaticDrawingBuildCount++;
            drawing.DrawRectangle(surfaceBrush, null, new(Bounds.Size));
            foreach (var clip in clips)
            {
                if (clip.CueId != timingPreview?.CueId || !clip.CueId.HasValue)
                {
                    DrawClip(drawing, clip.Start, clip.End, clip.Row, clip.Subtitle);
                }
            }
        });
        if (timingPreview is { } preview && clipsByCue.TryGetValue(preview.CueId, out var previewClips))
        {
            foreach (var index in previewClips)
            {
                var clip = clips[index];
                DrawClip(context, Seconds(preview.Start), Seconds(preview.End), clip.Row, clip.Subtitle);
            }
        }
        var rectangle = ViewportRectangle;
        context.DrawRectangle(viewportBrush, viewportBorder, rectangle);
        context.DrawLine(viewportEdge, rectangle.TopLeft, rectangle.BottomLeft);
        context.DrawLine(viewportEdge, rectangle.TopRight, rectangle.BottomRight);
        var playhead = Seconds(position) / duration * Bounds.Width;
        context.DrawLine(playheadPen, new(playhead, 0), new(playhead, Bounds.Height));
    }

    private void DrawClip(DrawingContext context, double start, double end, int row, bool subtitle)
    {
        var band = Math.Max(0, Bounds.Height - 6) / Math.Max(1, document.SubtitleTracks.Length + 1);
        var x = start / duration * Bounds.Width;
        var width = Math.Max(1, (end - start) / duration * Bounds.Width);
        context.DrawRectangle(subtitle ? subtitleBrush : layerBrush, null, new(x, 3 + row * band, width, band * 0.75));
    }

    private void RefreshDrawingPalette()
    {
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        surfaceBrush = new SolidColorBrush(Color.Parse(dark ? "#182233" : "#DEE6F0"));
        subtitleBrush = new SolidColorBrush(Color.Parse(dark ? "#7396D9" : "#557EB9"));
        layerBrush = new SolidColorBrush(Color.Parse(dark ? "#62B6B2" : "#378B85"));
        playheadPen = new(new SolidColorBrush(Color.Parse(dark ? "#FF6B7A" : "#B52542")), 2);
        clipDrawing.Dispose();
        InvalidateVisual();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelGesture();
        clipDrawing.Dispose();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGesture();
        clipDrawing.Dispose();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || Bounds.Width <= 0 || viewport.Width <= 0)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        var rectangle = ViewportRectangle;
        var startDistance = Math.Abs(point.X - rectangle.Left);
        var endDistance = Math.Abs(point.X - rectangle.Right);
        dragMode = Math.Min(startDistance, endDistance) > 7 ? TimelineOverviewDragMode.MOVE :
            startDistance < endDistance ? TimelineOverviewDragMode.START : TimelineOverviewDragMode.END;
        if (dragMode == TimelineOverviewDragMode.MOVE && !rectangle.Contains(point))
        {
            Publish(viewport with { StartSeconds = point.X / Bounds.Width * duration - viewport.VisibleDuration / 2 });
        }

        original = viewport;
        pointerOrigin = point.X;
        capturedPointer = e.Pointer;
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragMode == TimelineOverviewDragMode.NONE)
        {
            return;
        }

        var delta = (e.GetPosition(this).X - pointerOrigin) / Math.Max(1, Bounds.Width) * duration;
        var minimum = Math.Max(0.000001, original.Width / TimelineViewport.MAX_PIXELS_PER_SECOND);
        var start = original.StartSeconds;
        var end = start + original.VisibleDuration;
        switch (dragMode)
        {
            case TimelineOverviewDragMode.MOVE:
                Publish(original with { StartSeconds = start + delta });
                break;
            case TimelineOverviewDragMode.START:
                start = Math.Clamp(start + delta, 0, Math.Max(0, end - minimum));
                Publish(original with
                {
                    StartSeconds = start,
                    PixelsPerSecond = original.Width / Math.Max(minimum, end - start)
                }, true);
                break;
            case TimelineOverviewDragMode.END:
                end = Math.Clamp(end + delta, start + minimum, Math.Max(start + minimum, Math.Max(duration, end)));
                Publish(original with { PixelsPerSecond = original.Width / Math.Max(minimum, end - start) }, true);
                break;
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        CancelGesture();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelGesture();
    }

    internal void CancelGesture()
    {
        dragMode = TimelineOverviewDragMode.NONE;
        var pointer = capturedPointer;
        capturedPointer = null;
        pointer?.Capture(null);
    }

    private void Publish(TimelineViewport value, bool preserveTimeWindow = false)
    {
        viewport = preserveTimeWindow
            ? value.Resize(value.Width, value.Height, duration, double.MaxValue)
            : value.Normalize(duration, double.MaxValue);
        InvalidateVisual();
        ViewportChanged?.Invoke(this, new(viewport));
    }

    private static IEnumerable<ProjectLayer> Flatten(IEnumerable<ProjectLayer> layers)
    {
        foreach (var layer in layers)
        {
            yield return layer;
            foreach (var child in Flatten(layer.Children))
            {
                yield return child;
            }
        }
    }

    private static double Seconds(MediaTime value) => (double)value.Numerator / value.Denominator;
}
