using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>共享时间几何的计时组轴；独立编辑起点、终点与位置，释放提交一次范围请求。</summary>
public sealed class KaraokeClipAxis : Control
{
    /// <summary>可绑定的吸附开关元数据。</summary>
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Avalonia styled property metadata uses the public NameProperty convention.")]
    public static readonly StyledProperty<bool> IsSnapEnabledProperty =
        AvaloniaProperty.Register<KaraokeClipAxis, bool>(nameof(IsSnapEnabled), true);
    /// <summary>控制非拖拽期间是否常驻显示片段时长。</summary>
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Avalonia styled property metadata uses the public NameProperty convention.")]
    public static readonly StyledProperty<bool> KeepDurationLabelsVisibleProperty =
        AvaloniaProperty.Register<KaraokeClipAxis, bool>(nameof(KeepDurationLabelsVisible));
    private const double EDGE_PADDING = 12;
    private const double TRACK_TOP = 22;
    private const double LANE_HEIGHT = 42;
    private const double LANE_PITCH = 50;
    private SubtitleLine? line;
    private MediaTime offset;
    private Guid? selectedId;
    private Guid? draggingId;
    private SubtitleLine? frozen;
    private MediaTime frozenOffset;
    private TimelineViewport viewport = new();
    private TimelineViewport? frozenViewport;
    private ImmutableDictionary<Guid, int> lanes = ImmutableDictionary<Guid, int>.Empty;
    private Dictionary<Guid, KaraokeSegment> clipsById = [];
    private bool fitted = true;
    private double pointerStart;
    private MediaTime delta;
    private IPointer? capturedPointer;
    private IReadOnlyList<MediaTime> snapBoundaries = [];
    private MediaTime? snapTarget;
    private bool pointerMoved;
    private KaraokeAxisGestureKind? gestureKind;
    private readonly KaraokeDurationLabelsAdorner durationLabelsAdorner = new();
    private AdornerLayer? durationLabelLayer;
    private readonly List<Visual> visibilityAncestors = [];
    internal IReadOnlyList<KaraokeDurationLabel> DurationLabels { get; private set; } = [];
    internal TimelineViewport Viewport => frozenViewport ?? viewport;
    internal IReadOnlyDictionary<Guid, int> ClipLanes => lanes;
    internal bool HasActiveGesture => draggingId is not null;
    internal MediaTime? SnapTarget => snapTarget;
    private double Seconds => line is null ? 1 : ToSeconds(line.End - line.Start);
    private MediaTime ActiveOffset => frozen is not null ? frozenOffset : offset;
    private MediaTime DomainMinimum => Min(ActiveOffset, MediaTime.Zero);
    private double ContentDuration => Math.Max(0.001, Math.Max(ToSeconds(offset) + Seconds,
        line?.Karaoke.Select(clip => ToSeconds(clip.End)).DefaultIfEmpty(0).Max() ?? 0) - ToSeconds(DomainMinimum));
    private double ContentHeight => Math.Max(1, lanes.Values.DefaultIfEmpty(0).Max() + 1) * LANE_PITCH - 8;
    private double NavigationDuration => ContentDuration + Viewport.VisibleDuration;
    private Rect TrackBounds => new(EDGE_PADDING, TRACK_TOP, Math.Max(1, Bounds.Width - EDGE_PADDING * 2),
        Math.Max(1, Bounds.Height - TRACK_TOP - 22));

    /// <summary>创建可捕获本地指针、缩放及滚动的卡拉 OK 计时轴。</summary>
    public KaraokeClipAxis()
    {
        MinHeight = 88;
        Height = 88;
        ClipToBounds = true;
        Focusable = true;
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnMagnify);
    }

    /// <summary>请求选中计时组。</summary>
    public event EventHandler<KaraokeClipSelectionEventArgs>? ClipSelectionRequested;
    /// <summary>释放拖拽后基于冻结数据提交一次范围请求。</summary>
    public event EventHandler<KaraokeClipRangeEventArgs>? RangeRequested;
    /// <summary>单击片段请求弹出属性。</summary>
    public event EventHandler<KaraokeClipEditRequestedEventArgs>? ClipEditRequested;
    /// <summary>启用十毫秒网格和邻近时间边界吸附；Alt 暂时绕过。</summary>
    public bool IsSnapEnabled
    {
        get => GetValue(IsSnapEnabledProperty);
        set => SetValue(IsSnapEnabledProperty, value);
    }
    /// <summary>常驻显示实际时长；关闭后仅在拖拽期间显示。</summary>
    public bool KeepDurationLabelsVisible
    {
        get => GetValue(KeepDurationLabelsVisibleProperty);
        set => SetValue(KeepDurationLabelsVisibleProperty, value);
    }
    /// <summary>是否存在超出字幕可见结束边界的计时组。</summary>
    public bool HasOverflow => line is not null && line.Karaoke.Any(clip => PreviewRange(clip).End > offset + line.End - line.Start);

    /// <summary>同步数据；外部字幕、内容偏移及选择变化取消未完成的拖拽。</summary>
    public void SetContent(SubtitleLine? value, MediaTime animationOffset, Guid? selectedClipId)
    {
        var changed = value != line || animationOffset != offset;
        var targetChanged = value?.Id != line?.Id || animationOffset != offset;
        if (changed || draggingId is not null && selectedClipId != selectedId)
        {
            CancelGesture();
        }
        line = value;
        offset = animationOffset;
        selectedId = selectedClipId;
        if (changed)
        {
            lanes = KaraokeAxisLaneAllocator.Allocate(value?.Karaoke ?? []);
            clipsById = value?.Karaoke.ToDictionary(clip => clip.Id) ?? [];
            if (targetChanged)
            {
                fitted = true;
            }
            RefreshViewport();
        }
        UpdateDurationLabels();
        InvalidateVisual();
    }

    /// <summary>将全部计时组与字幕可见窗口适配到轴宽度；不产生文档编辑。</summary>
    public void FitToContent()
    {
        if (HasActiveGesture)
        {
            return;
        }
        fitted = true;
        RefreshViewport();
        UpdateDurationLabels();
        InvalidateVisual();
    }

    /// <summary>丢弃尚未释放的手势，不发出业务请求。</summary>
    public void CancelGesture()
    {
        var pointer = capturedPointer;
        capturedPointer = null;
        draggingId = null;
        frozen = null;
        frozenViewport = null;
        gestureKind = null;
        delta = MediaTime.Zero;
        snapTarget = null;
        snapBoundaries = [];
        pointerMoved = false;
        pointer?.Capture(null);
        RefreshViewport();
        UpdateDurationLabels();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == BoundsProperty && !HasActiveGesture)
        {
            RefreshViewport();
        }
        if (change.Property == IsVisibleProperty && !IsVisible || change.Property == IsEnabledProperty && !IsEnabled)
        {
            CancelGesture();
        }
        if (change.Property == KeepDurationLabelsVisibleProperty || change.Property == BoundsProperty || change.Property == IsVisibleProperty)
        {
            UpdateDurationLabels();
        }
    }

    private void RefreshViewport()
    {
        var width = TrackBounds.Width;
        var height = TrackBounds.Height;
        viewport = fitted
            ? new TimelineViewport(0, width / ContentDuration, 0, width, height).Normalize(ContentDuration, ContentHeight)
            : viewport.Resize(width, height, NavigationDuration, ContentHeight);
    }

    private void SetViewport(TimelineViewport value)
    {
        if (HasActiveGesture)
        {
            return;
        }
        fitted = false;
        viewport = value;
        UpdateDurationLabels();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (!HasActiveGesture)
        {
            if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                SetViewport(viewport.ZoomAt(Math.Exp(Math.Clamp(e.Delta.Y, -20, 20) * 0.12),
                    e.GetPosition(this).X - EDGE_PADDING, NavigationDuration, ContentHeight));
            }
            else
            {
                var horizontal = -e.Delta.X * 48;
                var vertical = -e.Delta.Y * 36;
                if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
                {
                    horizontal += -e.Delta.Y * 48;
                    vertical = 0;
                }
                SetViewport(viewport.Pan(horizontal, vertical, NavigationDuration, ContentHeight));
            }
        }
        e.Handled = true;
    }

    private void OnMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (!HasActiveGesture)
        {
            SetViewport(viewport.ZoomAt(Math.Exp(Math.Clamp(e.Delta.X, -2, 2)),
                e.GetPosition(this).X - EDGE_PADDING, NavigationDuration, ContentHeight));
        }
        e.Handled = true;
    }

    private static double ToSeconds(MediaTime value) => (double)value.Numerator / value.Denominator;
    internal MediaTime VisibleRelativeTime(MediaTime contentTime) => contentTime - offset;
    private double X(MediaTime time) => EDGE_PADDING + (ToSeconds(time - DomainMinimum) - Viewport.StartSeconds) * Viewport.PixelsPerSecond;
    private (MediaTime Start, MediaTime End) PreviewRange(KaraokeSegment clip)
    {
        return clip.Id != draggingId ? (clip.Start, clip.End) : gestureKind switch
        {
            KaraokeAxisGestureKind.START => (clip.Start + delta, clip.End),
            KaraokeAxisGestureKind.END => (clip.Start, clip.End + delta),
            KaraokeAxisGestureKind.MOVE => (clip.Start + delta, clip.End + delta),
            _ => (clip.Start, clip.End)
        };
    }

    internal KaraokeAxisClipGeometry GeometryFor(Guid clipId)
    {
        var clip = clipsById[clipId];
        var range = PreviewRange(clip);
        var start = X(range.Start);
        var end = X(range.End);
        var y = TRACK_TOP + lanes[clip.Id] * LANE_PITCH - Viewport.VerticalOffset;
        var timeBounds = new Rect(start, y, Math.Max(0, end - start), LANE_HEIGHT);
        var body = new Rect(timeBounds.Center.X - Math.Max(1, timeBounds.Width) / 2, y,
            Math.Max(1, timeBounds.Width), LANE_HEIGHT);
        var floating = clip.Id == selectedId && timeBounds.Width < 28 && body.Intersects(TrackBounds);
        var left = floating ? Math.Clamp(timeBounds.Center.X - 18, TrackBounds.Left + 10,
            Math.Max(TrackBounds.Left + 10, TrackBounds.Right - 41)) : start + Math.Min(3, timeBounds.Width / 4);
        var right = floating ? left + 36 : end - Math.Min(3, timeBounds.Width / 4);
        return new(timeBounds, body, new(left - 5, y + 7, 10, LANE_HEIGHT - 14),
            new(right - 5, y + 7, 10, LANE_HEIGHT - 14));
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(22, 120, 120, 120)), null, TrackBounds);
        if (line is null)
        {
            return;
        }
        using (context.PushClip(TrackBounds))
        {
            var visibleStart = X(offset);
            var visibleEnd = X(offset + line.End - line.Start);
            var visible = new Rect(Math.Clamp(visibleStart, TrackBounds.Left, TrackBounds.Right), TrackBounds.Top,
                Math.Max(0, Math.Min(visibleEnd, TrackBounds.Right) - Math.Max(visibleStart, TrackBounds.Left)), TrackBounds.Height);
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(18, 30, 144, 255)), null, visible);
            foreach (var clip in line.Karaoke.OrderBy(value => value.Id == selectedId))
            {
                DrawClip(context, clip);
            }
            foreach (var edge in new[] { offset, offset + line.End - line.Start })
            {
                var x = X(edge);
                context.DrawLine(new Pen(Brushes.IndianRed), new(x, TrackBounds.Top), new(x, TrackBounds.Bottom));
            }
            if (IsSnapEnabled && snapTarget is { } snapped)
            {
                var x = X(snapped);
                context.DrawRectangle(new SolidColorBrush(Color.FromArgb(64, 30, 144, 255)), new Pen(Brushes.DodgerBlue),
                    new(x - 2, TrackBounds.Top, 4, TrackBounds.Height));
            }
        }
        DrawRuler(context);
    }

    private void DrawClip(DrawingContext context, KaraokeSegment clip)
    {
        var geometry = GeometryFor(clip.Id);
        if (!geometry.Body.Intersects(TrackBounds))
        {
            return;
        }
        var selected = clip.Id == selectedId;
        context.DrawRectangle(new SolidColorBrush(selected ? Color.Parse("#496CA9") : Color.Parse("#58717D")),
            new Pen(selected ? Brushes.DodgerBlue : Brushes.Gray, selected ? 2 : 1), geometry.Body);
        using var text = WorkbenchTextFormatting.CreateLayout(this, line!.Text.Substring(clip.Utf16Start, clip.Utf16Length), 14,
            Brushes.White, maximumWidth: Math.Max(1, geometry.Body.Width - 8));
        using (context.PushClip(geometry.Body))
        {
            text.Draw(context, WorkbenchTextFormatting.CenteredOrigin(text,
                new(geometry.Body.X + 4, geometry.Body.Y, Math.Max(1, geometry.Body.Width - 8), geometry.Body.Height)));
        }
        foreach (var handle in new[] { geometry.StartHandle, geometry.EndHandle })
        {
            if (selected && geometry.TimeBounds.Width < 28)
            {
                context.DrawLine(new Pen(Brushes.DodgerBlue), geometry.TimeBounds.Center, handle.Center);
                context.DrawRectangle(new SolidColorBrush(Color.Parse("#496CA9")), new Pen(Brushes.DodgerBlue), handle, 2, 2);
            }
            context.DrawLine(new Pen(Brushes.White), new(handle.Center.X, handle.Top + 3), new(handle.Center.X, handle.Bottom - 3));
        }
    }

    private void DrawRuler(DrawingContext context)
    {
        var y = Bounds.Height - 20;
        foreach (var tick in RulerTicks())
        {
            context.DrawLine(new Pen(Brushes.Gray), new(tick.X, y), new(tick.X, y + 3));
            using var text = WorkbenchTextFormatting.CreateLayout(this, tick.Label, 11, Brushes.Gray);
            text.Draw(context, new(Math.Clamp(tick.X + 2, EDGE_PADDING,
                Math.Max(EDGE_PADDING, Bounds.Width - EDGE_PADDING - text.Width)), y + 3));
        }
    }

    internal IReadOnlyList<(double Seconds, string Label, double X)> RulerTicks()
    {
        var first = Viewport.StartSeconds + ToSeconds(DomainMinimum - offset);
        var step = TimelineTimeScale.MajorStep(Viewport.PixelsPerSecond);
        var last = first + Viewport.VisibleDuration;
        var start = Math.Ceiling(first / step) * step;
        var ticks = new List<(double Seconds, string Label, double X)>();
        for (var index = 0; index <= Math.Ceiling(Viewport.VisibleDuration / step); index++)
        {
            var value = start + index * step;
            if (value > last)
            {
                break;
            }
            var x = EDGE_PADDING + (value - first) * Viewport.PixelsPerSecond;
            ticks.Add((value, value.ToString("0.###", CultureInfo.InvariantCulture) + " s", x));
        }
        return ticks;
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (line is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || !TrackBounds.Contains(e.GetPosition(this)))
        {
            return;
        }
        var point = e.GetPosition(this);
        var source = line;
        var sourceOffset = offset;
        foreach (var clip in source.Karaoke.OrderByDescending(value => value.Id == selectedId)
                     .ThenBy(value => Math.Abs(GeometryFor(value.Id).TimeBounds.Center.X - point.X)))
        {
            var geometry = GeometryFor(clip.Id);
            var start = geometry.StartHandle.Contains(point);
            var end = geometry.EndHandle.Contains(point);
            var bodyHit = new Rect(geometry.Body.Center.X - Math.Max(8, geometry.Body.Width) / 2,
                geometry.Body.Y, Math.Max(8, geometry.Body.Width), geometry.Body.Height).Contains(point);
            if (!start && !end && !bodyHit)
            {
                continue;
            }
            var kind = start && (!end || point.X < geometry.TimeBounds.Center.X)
                ? KaraokeAxisGestureKind.START : end ? KaraokeAxisGestureKind.END : KaraokeAxisGestureKind.MOVE;
            ClipSelectionRequested?.Invoke(this, new(clip.Id));
            if (selectedId != clip.Id || line != source || offset != sourceOffset)
            {
                return;
            }
            Focus();
            frozenViewport = viewport;
            draggingId = clip.Id;
            frozen = source;
            frozenOffset = sourceOffset;
            gestureKind = kind;
            snapBoundaries = source.Karaoke.Where(item => item.Id != clip.Id).SelectMany(item => new[] { item.Start, item.End })
                .Append(MediaTime.Zero).Append(offset).Append(offset + source.End - source.Start)
                .Where(boundary => boundary >= MediaTime.Zero).Distinct().Order().ToArray();
            pointerStart = PointerX(e);
            pointerMoved = false;
            delta = MediaTime.Zero;
            capturedPointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
            return;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (draggingId is not { } id || frozen is null || e.Pointer != capturedPointer)
        {
            return;
        }
        var pointerDelta = PointerX(e) - pointerStart;
        pointerMoved |= Math.Abs(pointerDelta) >= 4;
        if (!pointerMoved)
        {
            return;
        }
        delta = new(checked((long)Math.Round(pointerDelta / Viewport.PixelsPerSecond * TimeSpan.TicksPerSecond)), TimeSpan.TicksPerSecond);
        snapTarget = null;
        var clip = clipsById[id];
        var duration = clip.End - clip.Start;
        var minimum = duration < new MediaTime(1, TimeSpan.TicksPerSecond) ? duration : new(1, TimeSpan.TicksPerSecond);
        if (IsSnapEnabled && (e.KeyModifiers & KeyModifiers.Alt) == 0)
        {
            var edge = gestureKind == KaraokeAxisGestureKind.END ? clip.End : clip.Start;
            delta = TimelineQuantization.Quantize(edge + delta, new(1, 100)) - edge;
            var boundaries = gestureKind switch
            {
                KaraokeAxisGestureKind.START => snapBoundaries.Where(value => value <= clip.End - minimum).ToArray(),
                KaraokeAxisGestureKind.END => snapBoundaries.Where(value => value >= clip.Start + minimum).ToArray(),
                _ => snapBoundaries
            };
            var snap = gestureKind == KaraokeAxisGestureKind.MOVE
                ? TimelineQuantization.ResolveSnapOffset(clip.Start + delta, clip.End + delta, boundaries, Viewport.PixelsPerSecond)
                : TimelineQuantization.ResolveSnap(edge + delta, boundaries, Viewport.PixelsPerSecond);
            delta = gestureKind == KaraokeAxisGestureKind.MOVE ? delta + snap.Value : snap.Value - edge;
            snapTarget = snap.Boundary;
        }
        var unclamped = delta;
        delta = gestureKind switch
        {
            KaraokeAxisGestureKind.START => Max(-clip.Start, Min(delta, duration - minimum)),
            KaraokeAxisGestureKind.END => Max(delta, minimum - duration),
            _ => Max(delta, -clip.Start)
        };
        if (unclamped != delta)
        {
            snapTarget = null;
        }
        UpdateDurationLabels();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (draggingId is { } id && frozen is { } source && e.Pointer == capturedPointer)
        {
            var clip = clipsById[id];
            var range = PreviewRange(clip);
            var sourceOffset = frozenOffset;
            var editing = !pointerMoved;
            var changed = range.Start != clip.Start || range.End != clip.End;
            var anchor = GeometryFor(id).Body;
            CancelGesture();
            if (editing)
            {
                ClipEditRequested?.Invoke(this, new(source.Id, id, anchor));
            }
            else if (changed)
            {
                RangeRequested?.Invoke(this, new(source, sourceOffset, id, range.Start, range.End));
            }
            e.Handled = true;
        }
        base.OnPointerReleased(e);
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        CancelGesture();
        base.OnPointerCaptureLost(e);
    }

    private double PointerX(PointerEventArgs e) => e.GetPosition(TopLevel.GetTopLevel(this) ?? (Visual)this).X;
    private static MediaTime Min(MediaTime first, MediaTime second) => first < second ? first : second;
    private static MediaTime Max(MediaTime first, MediaTime second) => first > second ? first : second;

    private void UpdateDurationLabels()
    {
        var source = frozen ?? line;
        if (!IsVisible || visibilityAncestors.Any(ancestor => !ancestor.IsVisible) || Bounds.Width <= 0 || source is null ||
            !KeepDurationLabelsVisible && !(pointerMoved && frozen is not null))
        {
            DurationLabels = [];
            durationLabelsAdorner.SetLabels(DurationLabels);
            return;
        }
        var candidates = new List<KaraokeDurationLabel>();
        foreach (var clip in source.Karaoke)
        {
            var rect = GeometryFor(clip.Id).Body;
            if (!rect.Intersects(TrackBounds))
            {
                continue;
            }
            var range = PreviewRange(clip);
            var duration = range.End - range.Start;
            var value = ((decimal)duration.Numerator / duration.Denominator).ToString("0.#######", CultureInfo.InvariantCulture) + " s";
            using var text = WorkbenchTextFormatting.CreateLayout(this, value, 11, Brushes.White, lineHeight: 20);
            var width = text.Width + 8;
            candidates.Add(new(clip.Id, duration, value, new(rect.Center.X - width / 2, 1, width, 20)));
        }
        var rows = new List<double>();
        var labels = new List<KaraokeDurationLabel>();
        foreach (var label in candidates.OrderBy(value => value.Bounds.Left).ThenBy(value => value.ClipId))
        {
            var row = rows.FindIndex(right => right + 4 <= label.Bounds.Left);
            if (row < 0)
            {
                row = rows.Count;
                rows.Add(label.Bounds.Right);
            }
            else
            {
                rows[row] = label.Bounds.Right;
            }
            labels.Add(label with { Bounds = new(label.Bounds.X, 1 - row * 20, label.Bounds.Width, 20) });
        }
        DurationLabels = labels;
        durationLabelsAdorner.SetLabels(DurationLabels);
    }

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        foreach (var ancestor in this.GetVisualAncestors())
        {
            visibilityAncestors.Add(ancestor);
            ancestor.PropertyChanged += OnVisibilityAncestorChanged;
        }
        durationLabelLayer = AdornerLayer.GetAdornerLayer(this);
        if (durationLabelLayer is not null)
        {
            AdornerLayer.SetAdornedElement(durationLabelsAdorner, this);
            AdornerLayer.SetIsClipEnabled(durationLabelsAdorner, false);
            durationLabelLayer.Children.Add(durationLabelsAdorner);
        }
        UpdateDurationLabels();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CancelGesture();
        foreach (var ancestor in visibilityAncestors)
        {
            ancestor.PropertyChanged -= OnVisibilityAncestorChanged;
        }
        visibilityAncestors.Clear();
        DurationLabels = [];
        durationLabelsAdorner.SetLabels(DurationLabels);
        durationLabelLayer?.Children.Remove(durationLabelsAdorner);
        durationLabelLayer = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void OnVisibilityAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty)
        {
            if (!IsEffectivelyVisible)
            {
                CancelGesture();
            }
            UpdateDurationLabels();
        }
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && draggingId is not null)
        {
            CancelGesture();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
