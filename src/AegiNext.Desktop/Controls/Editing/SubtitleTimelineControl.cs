using System.Globalization;
using System.Collections.Immutable;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Editing;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Controls;

/// <summary>语谱图上的字幕区间及效果关键帧，拖动只在完成时提交编辑。</summary>
public sealed class SubtitleTimelineControl : Control, IDisposable
{
    private ProjectDocument document = new();
    private SpectrogramData? spectrum;
    private WriteableBitmap? spectrumBitmap;
    private Guid? selectedCue;
    private ProjectLayer? selectedLayer;
    private MediaTime position;
    private TimelineViewport viewport = new();
    private readonly HashSet<Guid> selectedIds = [];
    private readonly HashSet<Guid> collapsedTracks = [];
    private readonly HashSet<Guid> collapsedGroups = [];
    private readonly HashSet<Guid> collapsedAnimations = [];
    private IReadOnlyList<TimelineRow> rows = [];
    private double duration = 60;
    private Guid? pendingTrackId;
    private Guid? originalTrackId;
    private bool validDrop = true;
    private double contentHeight;
    private TimelineDragMode dragMode;
    private Guid dragId;
    private double dragPointer;
    private MediaTime originalStart;
    private MediaTime originalEnd;
    private MediaTime pendingStart;
    private MediaTime pendingEnd;
    private MediaTime originalKey;
    private MediaTime pendingKey;
    private double originalValue;
    private double pendingValue;
    private double valueMinimum;
    private double valueMaximum;
    private bool stretching;
    private AnimationProperty effectProperty = AnimationProperty.OPACITY;
    private IPointer? capturedPointer;
    private MediaTime? lastSeekRequest;

    /// <summary>创建可交互时间线。</summary>
    public SubtitleTimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
        SizeChanged += (_, _) => RefreshMetrics();
        AddHandler(PointerTouchPadGestureMagnifyEvent, OnMagnify);
    }

    public event EventHandler<TimelineSeekEventArgs>? SeekRequested;
    public event EventHandler<TimelineSelectionEventArgs>? CueSelected;
    public event EventHandler<TimelineSelectionEventArgs>? LayerSelected;
    public event EventHandler<TimelineTimingEventArgs>? TimingChanged;
    public event EventHandler<TimelineKeyframeEventArgs>? KeyframeMoved;
    public event EventHandler<TimelineKeyframeEventArgs>? KeyframeSelected;
    public event EventHandler<TimelineSelectionEventArgs>? ClipSelectionChanged;
    public event EventHandler<TimelineSelectionEventArgs>? TrackSelected;
    public event EventHandler<TimelineViewportEventArgs>? ViewportChanged;

    public new AnimationProperty EffectProperty
    {
        get => effectProperty;
        set
        {
            if (effectProperty != value)
            {
                CancelDrag();
                effectProperty = value;
                InvalidateVisual();
            }
        }
    }

    internal bool HasActiveDrag => dragMode != TimelineDragMode.NONE;
    internal bool IsSeeking => dragMode == TimelineDragMode.SEEK;

    public MediaTime Position
    {
        get => position;
        set
        {
            if (position == value || IsSeeking)
            {
                return;
            }

            position = value;
            InvalidateVisual();
        }
    }

    public double ViewStart
    {
        get => viewport.StartSeconds;
        set => SetViewport(viewport with { StartSeconds = value }, duration);
    }

    public double PixelsPerSecond
    {
        get => viewport.PixelsPerSecond;
        set => SetViewport(viewport with { PixelsPerSecond = value }, duration);
    }

    public double VisibleDuration => viewport.VisibleDuration;
    public TimelineViewport Viewport => viewport;
    internal double HeaderWidth => Math.Min(128, Bounds.Width * 0.25);
    internal double RulerHeight => Math.Min(24, Bounds.Height * 0.2);
    internal double ContentHeight => contentHeight;

    /// <summary>接收同一编辑视口和工程范围，不操作播放控制器。</summary>
    public void SetViewport(TimelineViewport value, double totalDuration)
    {
        duration = Math.Max(0.001, totalDuration);
        var next = value.Resize(Math.Max(0, Bounds.Width - HeaderWidth),
            Math.Max(0, Bounds.Height - RulerHeight), duration, ContentHeight);
        if (next != viewport)
        {
            viewport = next;
            InvalidateVisual();
        }
    }

    /// <summary>替换显示快照，不持有可变编辑器。</summary>
    public void SetDocument(ProjectDocument value, Guid? cueId, ProjectLayer? layer, IReadOnlyList<Guid>? selection = null)
    {
        var nextSelection = selection ?? (layer is null ? Array.Empty<Guid>() : [layer.Id]);
        if (ReferenceEquals(document, value) && selectedCue == cueId && ReferenceEquals(selectedLayer, layer) &&
            selectedIds.SetEquals(nextSelection))
        {
            return;
        }

        if (!ReferenceEquals(document, value) || selectedCue != cueId || selectedLayer?.Id != layer?.Id)
        {
            CancelDrag();
        }

        document = value;
        selectedCue = cueId;
        selectedLayer = layer;
        selectedIds.Clear();
        selectedIds.UnionWith(nextSelection);
        RebuildRows();
        EnsureSelectedRowVisible();
        InvalidateVisual();
    }

    /// <summary>建立一次有界显示位图，高频率在顶端。</summary>
    public unsafe void SetSpectrogram(SpectrogramData? value)
    {
        spectrumBitmap?.Dispose();
        spectrumBitmap = null;
        spectrum = value;
        if (value is not null)
        {
            spectrumBitmap = new(new(value.Width, value.Height), new Vector(96, 96), PixelFormat.Bgra8888,
                AlphaFormat.Opaque);
            using var target = spectrumBitmap.Lock();
            var levels = value.Levels.Span;
            for (var y = 0; y < value.Height; y++)
            {
                var row = new Span<byte>((void*)(target.Address + y * target.RowBytes), value.Width * 4);
                for (var x = 0; x < value.Width; x++)
                {
                    var intensity = levels[(value.Height - 1 - y) * value.Width + x] / 255.0;
                    var offset = x * 4;
                    row[offset] = (byte)Math.Clamp(22 + 150 * Math.Sin(intensity * Math.PI), 0, 255);
                    row[offset + 1] = (byte)Math.Clamp(28 + intensity * intensity * 220, 0, 255);
                    row[offset + 2] = (byte)Math.Clamp(18 + Math.Pow(intensity, 3) * 237, 0, 255);
                    row[offset + 3] = 255;
                }
            }
        }

        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var foreground = new SolidColorBrush(Color.Parse(dark ? "#DDE3EE" : "#263247"));
        var grid = new Pen(new SolidColorBrush(Color.Parse(dark ? "#334155" : "#CCD5E2")));
        context.DrawRectangle(new SolidColorBrush(Color.Parse(dark ? "#131A26" : "#E7EDF5")), null, new(Bounds.Size));
        var body = new Rect(HeaderWidth, RulerHeight, Math.Max(0, Bounds.Width - HeaderWidth),
            Math.Max(0, Bounds.Height - RulerHeight));
        using (context.PushClip(body))
        {
            if (spectrum is not null && spectrumBitmap is not null)
            {
                var length = Seconds(spectrum.Duration);
                var start = Math.Clamp(ViewStart / length * spectrum.Width, 0, spectrum.Width);
                var visible = Math.Min(spectrum.Width - start, VisibleDuration / length * spectrum.Width);
                if (visible > 0)
                {
                    context.DrawImage(spectrumBitmap, new Rect(start, 0, visible, spectrum.Height),
                        new Rect(HeaderWidth, body.Top, visible / spectrum.Width * length * PixelsPerSecond, body.Height));
                }

                DrawWaveform(context, body.Top, body.Height);
            }

            foreach (var row in rows)
            {
                var y = RowY(row);
                if (y + row.Height < RulerHeight || y > Bounds.Height)
                {
                    continue;
                }

                context.DrawLine(grid, new(HeaderWidth, y + row.Height), new(Bounds.Width, y + row.Height));
                if (selectedLayer is { } layer && ClipsForRow(row).Any(clip => clip.Id == layer.Id) && row.CurveHeight > 0)
                {
                    DrawEffects(context, foreground, y);
                }

                foreach (var clip in ClipsForRow(row))
                {
                    var rectangle = ClipRectangle(clip, row);
                    if (rectangle.Right < HeaderWidth || rectangle.Left > Bounds.Width)
                    {
                        continue;
                    }

                    var active = selectedIds.Contains(clip.Id);
                    var invalid = HasActiveDrag && dragId == clip.Id && !validDrop;
                    context.DrawRectangle(new SolidColorBrush(Color.Parse(invalid ? "#BDAD4759" :
                            active ? "#B84568DC" : clip.Kind == LayerKind.SUBTITLE ? "#865F90C4" : "#864FB3AB")),
                        new Pen(new SolidColorBrush(Color.Parse(active ? "#A5B8FF" : "#77D4DF")), active ? 2 : 1), rectangle, 3, 3);
                    if (rectangle.Width > 8 && rectangle.Height >= 10)
                    {
                        var text = clip.SubtitleId is { } cueId ? document.Subtitles.First(cue => cue.Id == cueId).Text.Replace('\n', ' ') : clip.Name;
                        using (context.PushClip(rectangle.Deflate(3)))
                        {
                            DrawText(context, text, new(rectangle.X + 6, rectangle.Y + 3), Brushes.White, 11);
                        }
                    }
                }
            }
        }

        foreach (var row in rows)
        {
            var y = RowY(row);
            if (y + row.Height < RulerHeight || y > Bounds.Height)
            {
                continue;
            }

            using (context.PushClip(new Rect(0, RulerHeight, HeaderWidth, Math.Max(0, Bounds.Height - RulerHeight))))
            {
                context.DrawRectangle(new SolidColorBrush(Color.Parse(dark ? "#202C40" : "#D9E2EE")), null,
                    new(0, y, HeaderWidth, row.Height));
                DrawText(context, row.IsCollapsed ? "▸" : "▾", new(6 + row.Depth * 8, y + 5), foreground, 12);
                if (row.Clips.Any(clip => clip.Id == selectedLayer?.Id))
                {
                    DrawText(context, row.CurveHeight > 0 ? "◆" : "◇", new(28 + row.Depth * 8, y + 5), foreground, 12);
                }

                using (context.PushClip(new Rect(46 + row.Depth * 8, y, Math.Max(0, HeaderWidth - 48 - row.Depth * 8), row.Height)))
                {
                    DrawText(context, row.Name, new(46 + row.Depth * 8, y + 5), foreground, 11);
                }
            }
        }

        context.DrawRectangle(new SolidColorBrush(Color.Parse(dark ? "#131A26" : "#E7EDF5")), null,
            new(0, 0, Bounds.Width, RulerHeight));
        var desired = 70 / PixelsPerSecond;
        var exponent = Math.Pow(10, Math.Floor(Math.Log10(Math.Max(0.0001, desired))));
        var step = exponent * (desired / exponent <= 2 ? 2 : desired / exponent <= 5 ? 5 : 10);
        for (var tick = Math.Ceiling(ViewStart / step) * step; tick <= ViewStart + VisibleDuration; tick += step)
        {
            var x = X(tick);
            context.DrawLine(grid, new(x, RulerHeight - 4), new(x, Bounds.Height));
            DrawText(context, TimeSpan.FromSeconds(tick).ToString(tick >= 3600 ? @"hh\:mm\:ss" : @"mm\:ss", CultureInfo.InvariantCulture),
                new(x + 3, 2), foreground, 10);
        }

        var playhead = X(Seconds(position));
        if (playhead >= HeaderWidth)
        {
            context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#FF6B7A")), 2), new(playhead, 0), new(playhead, Bounds.Height));
        }

        if (ContentHeight > viewport.Height && viewport.Height > 0)
        {
            var thumbHeight = Math.Max(12, viewport.Height * viewport.Height / ContentHeight);
            var thumbY = RulerHeight + viewport.VerticalOffset / (ContentHeight - viewport.Height) * (viewport.Height - thumbHeight);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#998B9AB1")), null,
                new(Bounds.Width - 5, thumbY, 4, thumbHeight), 2, 2);
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        var row = RowAt(point.Y);
        if (point.X < HeaderWidth)
        {
            if (row is not null)
            {
                CancelDrag();
                if (point.X < 24 + row.Depth * 8)
                {
                    Toggle(row.TrackId.HasValue ? collapsedTracks : collapsedGroups, row.Id);
                    RebuildRows();
                    PublishViewport(viewport);
                }
                else if (point.X < 46 + row.Depth * 8 && selectedLayer is { } selected && row.Clips.Any(clip => clip.Id == selected.Id))
                {
                    Toggle(collapsedAnimations, selected.Id);
                    RebuildRows();
                    PublishViewport(viewport);
                }
                else if (row.TrackId is { } trackId)
                {
                    TrackSelected?.Invoke(this, new(trackId));
                }
                else if (row.Clips.Count > 0)
                {
                    SelectClip(row.Clips[0], e.KeyModifiers);
                }

                InvalidateVisual();
                e.Handled = true;
            }

            return;
        }

        dragPointer = point.X;
        stretching = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (selectedLayer is { } layer && CurveRectangle(layer.Id) is { } curve && curve.Contains(point))
        {
            var track = layer.Tracks.FirstOrDefault(item => item.Property == EffectProperty);
            if (track is not null)
            {
                (valueMinimum, valueMaximum) = ValueRange(track);
                foreach (var key in track.Keyframes)
                {
                    var keyPoint = GetKeyframePoint(layer.Id, key.Time, key.Value);
                    if (keyPoint is { } location && Math.Abs(point.X - location.X) <= 9 && Math.Abs(point.Y - location.Y) <= 9)
                    {
                        BeginKeyframeDrag(layer, key, point.X);
                        KeyframeSelected?.Invoke(this, new(layer.Id, EffectProperty, key.Time, key.Time));
                        if (HasActiveDrag)
                        {
                            capturedPointer = e.Pointer;
                            e.Pointer.Capture(this);
                        }

                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        if (row is not null)
        {
            foreach (var clip in row.Clips.Reverse())
            {
                var rectangle = ClipRectangle(clip, row);
                if (!rectangle.Contains(point))
                {
                    continue;
                }

                var mode = point.X - rectangle.Left < 8 ? TimelineDragMode.TRIM_START :
                    rectangle.Right - point.X < 8 ? TimelineDragMode.TRIM_END : TimelineDragMode.MOVE;
                var initialDocument = document;
                SelectClip(clip, e.KeyModifiers);
                if (!ReferenceEquals(initialDocument, document) ||
                    clip.Kind == LayerKind.GROUP ||
                    mode == TimelineDragMode.MOVE && (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0)
                {
                    e.Handled = true;
                    return;
                }

                BeginTimingDrag(clip.Id, clip.Start, clip.End, mode, point.X, stretching);
                originalTrackId = pendingTrackId = clip.SubtitleId is { } cueId
                    ? document.Subtitles.First(cue => cue.Id == cueId).TrackId : null;
                capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        if (point.Y >= RulerHeight)
        {
            return;
        }

        dragMode = TimelineDragMode.SEEK;
        lastSeekRequest = null;
        RequestSeek(TimeAt(point.X, e.KeyModifiers));
        if (HasActiveDrag)
        {
            capturedPointer = e.Pointer;
            e.Pointer.Capture(this);
        }

        e.Handled = true;
    }

    /// <inheritdoc />
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        if (HasActiveDrag)
        {
            return;
        }

        if ((e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            Zoom(Math.Exp(Math.Clamp(e.Delta.Y, -20, 20) * 0.12), e.GetPosition(this).X);
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

            PublishViewport(viewport.Pan(horizontal, vertical, duration, ContentHeight));
        }

        e.Handled = true;
    }

    private void OnMagnify(object? sender, PointerDeltaEventArgs e)
    {
        if (!HasActiveDrag)
        {
            Zoom(Math.Exp(Math.Clamp(e.Delta.X, -2, 2)), e.GetPosition(this).X);
            e.Handled = true;
        }
    }

    private void Zoom(double factor, double pointerX)
    {
        PublishViewport(viewport.ZoomAt(factor, pointerX - HeaderWidth, duration, ContentHeight));
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var point = e.GetPosition(this);
        var delta = TimeFromSeconds((point.X - dragPointer) / PixelsPerSecond, e.KeyModifiers);
        var minimum = new MediaTime(document.FrameRate.Denominator, document.FrameRate.Numerator);
        switch (dragMode)
        {
            case TimelineDragMode.SEEK:
                RequestSeek(TimeAt(point.X, e.KeyModifiers));
                break;
            case TimelineDragMode.MOVE:
                pendingStart = Max(MediaTime.Zero, originalStart + delta);
                pendingEnd = originalEnd + pendingStart - originalStart;
                break;
            case TimelineDragMode.TRIM_START:
                pendingStart = Max(MediaTime.Zero, Min(originalEnd - minimum, originalStart + delta));
                break;
            case TimelineDragMode.TRIM_END:
                pendingEnd = Max(originalStart + minimum, originalEnd + delta);
                break;
            case TimelineDragMode.KEYFRAME:
                pendingKey = selectedLayer is { } keyLayer
                    ? LayerAnimationTiming.ClampTime(keyLayer, originalKey + delta)
                    : originalKey;
                pendingValue = Math.Clamp(valueMinimum + (1 - (point.Y - EffectTop) / EffectHeight) *
                    (valueMaximum - valueMinimum), valueMinimum, valueMaximum);
                break;
        }

        if (dragMode is TimelineDragMode.MOVE or TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END)
        {
            UpdateTimingDrop(point.Y);
        }

        if (dragMode != TimelineDragMode.NONE)
        {
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        var mode = dragMode;
        if (mode == TimelineDragMode.SEEK)
        {
            RequestSeek(TimeAt(e.GetPosition(this).X, e.KeyModifiers));
        }

        var targetTrack = pendingTrackId;
        var canCommit = validDrop;
        CancelDrag();
        if (mode == TimelineDragMode.KEYFRAME && (pendingKey != originalKey || !pendingValue.Equals(originalValue)) &&
            selectedLayer is { } layer)
        {
            KeyframeMoved?.Invoke(this, new(layer.Id, EffectProperty, originalKey, pendingKey, pendingValue));
        }
        else if (mode is TimelineDragMode.MOVE or TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END &&
                 canCommit && (pendingStart != originalStart || pendingEnd != originalEnd || targetTrack != originalTrackId))
        {
            var editedLayer = Flatten(document.Layers).First(item => item.Id == dragId);
            TimingChanged?.Invoke(this, new(dragId, originalStart, pendingStart, pendingEnd,
                stretching ? TimelineEditMode.STRETCH : TimelineEditMode.CROP, mode == TimelineDragMode.MOVE)
            {
                SubtitleId = editedLayer.SubtitleId,
                TrackId = targetTrack
            });
        }

        RebuildRows();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        CancelDrag();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancelDrag();
        spectrumBitmap?.Dispose();
        spectrumBitmap = null;
    }

    internal void BeginTimingDrag(SubtitleLine cue, TimelineDragMode mode, double pointer, bool stretch)
    {
        BeginTimingDrag(cue.Id, cue.Start, cue.End, mode, pointer, stretch);
    }

    private void BeginTimingDrag(Guid id, MediaTime start, MediaTime end, TimelineDragMode mode, double pointer, bool stretch)
    {
        CancelDrag();
        dragMode = mode;
        dragId = id;
        dragPointer = pointer;
        stretching = stretch;
        originalStart = pendingStart = start;
        originalEnd = pendingEnd = end;
        validDrop = true;
    }

    internal void BeginKeyframeDrag(ProjectLayer layer, Keyframe key, double pointer)
    {
        CancelDrag();
        dragMode = TimelineDragMode.KEYFRAME;
        dragPointer = pointer;
        originalKey = pendingKey = key.Time;
        originalValue = pendingValue = key.Value;
        dragId = layer.Id;
    }

    internal void CancelGesture() => CancelDrag();

    private void CancelDrag()
    {
        var wasDragging = HasActiveDrag;
        dragMode = TimelineDragMode.NONE;
        lastSeekRequest = null;
        var pointer = capturedPointer;
        capturedPointer = null;
        pointer?.Capture(null);
        if (wasDragging)
        {
            InvalidateVisual();
        }
    }

    private void RequestSeek(MediaTime target)
    {
        position = target;
        InvalidateVisual();
        if (lastSeekRequest != target)
        {
            lastSeekRequest = target;
            SeekRequested?.Invoke(this, new(target));
        }
    }

    private void DrawEffects(DrawingContext context, IBrush foreground, double top)
    {
        var layer = selectedLayer!;
        if (dragMode == TimelineDragMode.MOVE && dragId == layer.Id)
        {
            layer = layer with { Start = pendingStart, End = pendingEnd };
        }
        else if (dragMode is TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END && dragId == layer.Id)
        {
            layer = LayerAnimationTiming.Retime(layer, pendingStart, pendingEnd,
                stretching ? TimelineEditMode.STRETCH : TimelineEditMode.CROP);
        }

        if (Bounds.Height >= 120)
        {
            DrawText(context, WorkbenchText.Property(EffectProperty), new(HeaderWidth + 5, top + 4), foreground, 12);
        }
        var track = layer.Tracks.FirstOrDefault(item => item.Property == EffectProperty);
        if (track is null)
        {
            return;
        }

        var (minimum, maximum) =
            dragMode == TimelineDragMode.KEYFRAME ? (valueMinimum, valueMaximum) : ValueRange(track);
        if (dragMode == TimelineDragMode.KEYFRAME)
        {
            track = track with
            {
                Keyframes = track.Keyframes.Where(key => key.Time != pendingKey || key.Time == originalKey)
                    .Select(key => key.Time == originalKey ? key with { Time = pendingKey, Value = pendingValue } : key)
                    .OrderBy(key => key.Time).ToImmutableArray()
            };
        }

        var curvePen = new Pen(new SolidColorBrush(Color.Parse("#CCD4FF")), 2);
        Point? previous = null;
        var startX = Math.Max(HeaderWidth, X(Seconds(layer.Start)));
        var endX = Math.Min(Bounds.Width, X(Seconds(layer.End)));
        if (endX <= startX)
        {
            return;
        }

        var samples = Math.Max(2, (int)(endX - startX) / 3);
        for (var sample = 0; sample <= samples; sample++)
        {
            var x = startX + sample * (endX - startX) / samples;
            var time = new MediaTime((long)Math.Round((ViewStart + (x - HeaderWidth) / PixelsPerSecond) * 1000000), 1000000) -
                layer.Start + layer.AnimationOffset;
            var point = new Point(x, ValueY(SceneEvaluator.EvaluateTrack(track, time), minimum, maximum));
            if (previous is { } first)
            {
                context.DrawLine(curvePen, first, point);
            }

            previous = point;
        }

        foreach (var key in track.Keyframes)
        {
            var time = dragMode == TimelineDragMode.KEYFRAME && key.Time == originalKey ? pendingKey : key.Time;
            var x = X(Seconds(layer.Start + time - layer.AnimationOffset));
            var value = dragMode == TimelineDragMode.KEYFRAME && key.Time == originalKey ? pendingValue : key.Value;
            var y = ValueY(value, minimum, maximum);
            var diamond = new StreamGeometry();
            var radius = Math.Min(6, Math.Max(2, EffectHeight / 5));
            using (var geometry = diamond.Open())
            {
                geometry.BeginFigure(new(x, y - radius));
                geometry.LineTo(new(x + radius, y));
                geometry.LineTo(new(x, y + radius));
                geometry.LineTo(new(x - radius, y));
                geometry.EndFigure(true);
            }

            context.DrawGeometry(new SolidColorBrush(Color.Parse("#B7C6FF")), new Pen(Brushes.White), diamond);
            if (Bounds.Height >= 120)
            {
                DrawText(context, value.ToString("0.##", CultureInfo.InvariantCulture), new(x + 8, y + 10), foreground, 11);
            }
        }
    }

    private static (double Minimum, double Maximum) ValueRange(AnimationTrack track)
    {
        if (track.Property is AnimationProperty.OPACITY or AnimationProperty.FILL_ALPHA
            or AnimationProperty.STROKE_ALPHA or AnimationProperty.PATH_PROGRESS)
        {
            return (0, 1);
        }

        var minimum = track.Keyframes.Min(frame => frame.Value);
        var maximum = track.Keyframes.Max(frame => frame.Value);
        var margin = Math.Max(1, (maximum - minimum) * 0.15);
        if (track.Property is AnimationProperty.BLUR or AnimationProperty.STROKE_WIDTH)
        {
            return (Math.Max(0, minimum - margin),
                Math.Min(track.Property == AnimationProperty.BLUR ? 512 : 4096, maximum + margin));
        }

        return (minimum - margin, maximum + margin);
    }

    private double ValueY(double value, double minimum, double maximum)
    {
        return EffectTop + (1 - (value - minimum) / (maximum - minimum)) * EffectHeight;
    }

    private void DrawWaveform(DrawingContext context, double top, double height)
    {
        var value = spectrum!;
        var data = value.Waveform.Span;
        var duration = Seconds(value.Duration);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#6079D5DB")));
        var center = top + height / 2;
        var first = Math.Clamp((int)(ViewStart / duration * value.Width), 0, value.Width - 1);
        var last = Math.Clamp((int)((ViewStart + VisibleDuration) / duration * value.Width), first, value.Width - 1);
        for (var index = first; index <= last; index++)
        {
            var x = X(index / (double)value.Width * duration);
            context.DrawLine(pen, new(x, center - data[index * 2 + 1] * height * 0.4),
                new(x, center - data[index * 2] * height * 0.4));
        }
    }

    internal double ClipTop => selectedLayer is { } layer && GetClipRectangle(layer.Id) is { } rectangle
        ? rectangle.Top : RulerHeight;
    internal double ClipHeight => selectedLayer is { } layer && GetClipRectangle(layer.Id) is { } rectangle
        ? rectangle.Height : 24;
    internal double EffectTop => selectedLayer is { } layer && CurveRectangle(layer.Id) is { } curve ? curve.Top : RulerHeight;
    internal double EffectHeight => selectedLayer is { } layer && CurveRectangle(layer.Id) is { } curve ? curve.Height : 1;

    /// <summary>返回实际绘制的片段范围；折叠的场景子节点返回 null。</summary>
    public Rect? GetClipRectangle(Guid layerId)
    {
        foreach (var row in rows)
        {
            if (ClipsForRow(row).FirstOrDefault(clip => clip.Id == layerId) is { } clip)
            {
                return ClipRectangle(clip, row);
            }
        }

        return null;
    }

    /// <summary>返回已展开属性曲线上关键帧的真实控件坐标。</summary>
    public Point? GetKeyframePoint(Guid layerId, MediaTime time, double value)
    {
        var layer = Flatten(document.Layers).FirstOrDefault(item => item.Id == layerId);
        var track = layer?.Tracks.FirstOrDefault(item => item.Property == EffectProperty);
        if (layer is null || track is null || CurveRectangle(layerId) is not { } curve)
        {
            return null;
        }

        var (minimum, maximum) = ValueRange(track);
        return new(X(Seconds(layer.Start + time - layer.AnimationOffset)),
            curve.Top + (1 - (value - minimum) / (maximum - minimum)) * curve.Height);
    }

    /// <summary>返回真实轨道头部的可见坐标，供面板和辅助功能导航。</summary>
    public Rect? GetTrackHeaderRectangle(Guid trackId)
    {
        return rows.FirstOrDefault(row => row.TrackId == trackId) is { } value
            ? new Rect(0, RowY(value), HeaderWidth, value.Height) : null;
    }

    private Rect? CurveRectangle(Guid layerId)
    {
        var row = rows.FirstOrDefault(item => ClipsForRow(item).Any(clip => clip.Id == layerId));
        return row is { CurveHeight: > 0 } ? new Rect(HeaderWidth, RowY(row) + 4,
            Math.Max(0, Bounds.Width - HeaderWidth), Math.Max(1, row.CurveHeight - 8)) : null;
    }

    private void RebuildRows()
    {
        var result = new List<TimelineRow>();
        var flattened = Flatten(document.Layers).ToArray();
        var byCue = flattened.Where(layer => layer.SubtitleId.HasValue).ToDictionary(layer => layer.SubtitleId!.Value);
        var top = 0d;
        foreach (var track in document.SubtitleTracks)
        {
            var clips = document.Subtitles.Where(cue => cue.TrackId == track.Id).OrderBy(cue => cue.Start)
                .Select(cue => byCue[cue.Id]).ToArray();
            var collapsed = collapsedTracks.Contains(track.Id);
            var animated = !collapsed && selectedLayer is { } selected && clips.Any(clip => clip.Id == selected.Id) &&
                !collapsedAnimations.Contains(selected.Id);
            var curve = animated ? Math.Min(88, Math.Max(0, Bounds.Height - RulerHeight - 30)) : 0;
            var height = 28 + curve;
            result.Add(new(track.Id, track.Id, track.Name, clips, 0, false, collapsed, top, height, curve));
            top += height;
        }

        AddSceneRows(document.Layers, 0, result, ref top);
        rows = result;
        contentHeight = top;
    }

    private void AddSceneRows(IEnumerable<ProjectLayer> layers, int depth, List<TimelineRow> result, ref double top)
    {
        foreach (var layer in layers)
        {
            if (layer.Kind == LayerKind.SUBTITLE)
            {
                continue;
            }

            var group = layer.Kind == LayerKind.GROUP;
            var collapsed = group && collapsedGroups.Contains(layer.Id);
            var curve = selectedLayer?.Id == layer.Id && !collapsedAnimations.Contains(layer.Id)
                ? Math.Min(88, Math.Max(0, Bounds.Height - RulerHeight - 30)) : 0;
            var height = 28 + curve;
            result.Add(new(layer.Id, null, layer.Name, [layer], depth, group, collapsed, top, height, curve));
            top += height;
            if (group && !collapsed)
            {
                AddSceneRows(layer.Children, depth + 1, result, ref top);
            }
        }
    }

    private IEnumerable<ProjectLayer> ClipsForRow(TimelineRow row)
    {
        foreach (var clip in row.Clips)
        {
            if (dragMode != TimelineDragMode.MOVE || clip.Id != dragId || !originalTrackId.HasValue || pendingTrackId == row.TrackId)
            {
                yield return clip;
            }
        }

        if (dragMode == TimelineDragMode.MOVE && originalTrackId.HasValue && pendingTrackId != originalTrackId && row.TrackId == pendingTrackId)
        {
            yield return Flatten(document.Layers).First(layer => layer.Id == dragId);
        }
    }

    private void RefreshMetrics()
    {
        RebuildRows();
        SetViewport(viewport, duration);
        ViewportChanged?.Invoke(this, new(viewport));
    }

    private void PublishViewport(TimelineViewport value)
    {
        SetViewport(value, duration);
        ViewportChanged?.Invoke(this, new(viewport));
    }

    private void EnsureSelectedRowVisible()
    {
        SetViewport(viewport, duration);
        var row = rows.FirstOrDefault(item => item.Clips.Any(clip => clip.Id == selectedLayer?.Id));
        if (row is null)
        {
            return;
        }

        var offset = viewport.VerticalOffset;
        if (row.Top < offset)
        {
            offset = row.Top;
        }
        else if (row.Top + row.Height > offset + viewport.Height)
        {
            offset = row.Top + row.Height - viewport.Height;
        }

        PublishViewport(viewport with { VerticalOffset = offset });
    }

    private double RowY(TimelineRow row) => RulerHeight + row.Top - viewport.VerticalOffset;
    private TimelineRow? RowAt(double y) => y < RulerHeight ? null : rows.FirstOrDefault(row => y >= RowY(row) && y < RowY(row) + row.Height);

    private Rect ClipRectangle(ProjectLayer layer, TimelineRow row)
    {
        var start = layer.Start;
        var end = layer.End;
        if (dragMode is TimelineDragMode.MOVE or TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END && dragId == layer.Id)
        {
            start = pendingStart;
            end = pendingEnd;
        }

        return new(X(Seconds(start)), RowY(row) + row.CurveHeight + 2,
            Math.Max(3, Seconds(end - start) * PixelsPerSecond), 24);
    }

    private void UpdateTimingDrop(double pointerY)
    {
        var layer = Flatten(document.Layers).First(item => item.Id == dragId);
        validDrop = true;
        if (layer.SubtitleId is not { } cueId || originalTrackId is not { } sourceTrack)
        {
            return;
        }

        pendingTrackId = dragMode == TimelineDragMode.MOVE ? RowAt(pointerY)?.TrackId : sourceTrack;
        if (pendingTrackId is not { } targetTrack)
        {
            validDrop = false;
            return;
        }

        var others = document.Subtitles.Where(cue => cue.Id != cueId && cue.TrackId == targetTrack).ToArray();
        if (targetTrack == sourceTrack)
        {
            var before = others.Where(cue => cue.End <= originalStart).Select(cue => cue.End).DefaultIfEmpty(MediaTime.Zero).Max();
            var after = others.Where(cue => cue.Start >= originalEnd).Select(cue => (MediaTime?)cue.Start).Min();
            switch (dragMode)
            {
                case TimelineDragMode.MOVE:
                    pendingStart = Max(before, after is { } nextStart
                        ? Min(pendingStart, nextStart - (originalEnd - originalStart)) : pendingStart);
                    pendingEnd = pendingStart + originalEnd - originalStart;
                    break;
                case TimelineDragMode.TRIM_START:
                    pendingStart = Max(before, pendingStart);
                    break;
                case TimelineDragMode.TRIM_END:
                    pendingEnd = after is { } boundary ? Min(boundary, pendingEnd) : pendingEnd;
                    break;
            }
        }

        validDrop = !others.Any(cue => pendingStart < cue.End && cue.Start < pendingEnd);
    }

    private void SelectClip(ProjectLayer clip, KeyModifiers modifiers)
    {
        if ((modifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
        {
            Toggle(selectedIds, clip.Id);
            if (selectedIds.Count == 0)
            {
                selectedIds.Add(clip.Id);
            }
        }
        else if ((modifiers & KeyModifiers.Shift) != 0 && selectedLayer is { } anchor)
        {
            var order = rows.SelectMany(row => row.Clips).ToArray();
            var first = Array.FindIndex(order, value => value.Id == anchor.Id);
            var last = Array.FindIndex(order, value => value.Id == clip.Id);
            if (first >= 0 && last >= 0)
            {
                selectedIds.Clear();
                selectedIds.UnionWith(order.Skip(Math.Min(first, last)).Take(Math.Abs(last - first) + 1).Select(value => value.Id));
            }
        }
        else
        {
            selectedIds.Clear();
            selectedIds.Add(clip.Id);
        }

        var primary = selectedIds.Contains(clip.Id) ? clip.Id : selectedIds.First();
        ClipSelectionChanged?.Invoke(this, new(primary, selectedIds.ToArray()));
        if (clip.SubtitleId is { } cueId)
        {
            CueSelected?.Invoke(this, new(cueId));
        }
        else
        {
            LayerSelected?.Invoke(this, new(primary, selectedIds.ToArray()));
        }
    }

    private static void Toggle(HashSet<Guid> values, Guid id)
    {
        if (!values.Remove(id))
        {
            values.Add(id);
        }
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

    private MediaTime TimeAt(double x, KeyModifiers modifiers) =>
        Max(MediaTime.Zero, TimeFromSeconds(ViewStart + (x - HeaderWidth) / PixelsPerSecond, modifiers));

    private MediaTime TimeFromSeconds(double value, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            return new((long)Math.Round(value * 1000), 1000);
        }

        var frame = (long)Math.Round(value * document.FrameRate.Numerator / document.FrameRate.Denominator);
        return new(checked(frame * document.FrameRate.Denominator), document.FrameRate.Numerator);
    }

    private double X(double time) => HeaderWidth + (time - ViewStart) * PixelsPerSecond;

    private static double Seconds(MediaTime time) => (double)time.Numerator / time.Denominator;

    private static MediaTime Max(MediaTime first, MediaTime second) => first >= second ? first : second;

    private static MediaTime Min(MediaTime first, MediaTime second) => first <= second ? first : second;

    private static void DrawText(DrawingContext context, string text, Point origin, IBrush foreground, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), size, foreground);
        context.DrawText(formatted, origin);
    }
}
