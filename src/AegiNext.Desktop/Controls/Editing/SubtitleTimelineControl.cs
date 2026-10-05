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
using AegiNext.Desktop.Settings;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Controls;

/// <summary>语谱图上的字幕区间及效果关键帧，拖动只在完成时提交编辑。</summary>
public sealed class SubtitleTimelineControl : Control, IDisposable
{
    private static readonly Cursor resizeCursor = new(StandardCursorType.SizeWestEast);
    private ProjectDocument document = new();
    private Dictionary<Guid, ProjectLayer> layersById = [];
    private Dictionary<Guid, SubtitleLine> cuesById = [];
    private Dictionary<Guid, TimelineRow> rowsByLayer = [];
    private readonly Dictionary<(Guid LayerId, AnimationProperty Property), (double Minimum, double Maximum)> valueRanges = [];
    private SpectrogramData? spectrum;
    private WriteableBitmap? spectrumBitmap;
    private AudioGraphPalette audioGraphPalette = new();
    private TimelineDrawingPalette drawingPalette = new(false);
    private Color[] spectrumColors = AudioGraphColorRamp.Create(new());
    private Pen waveformPen = new(new SolidColorBrush(AudioGraphColorRamp.Parse(new AudioGraphPalette().Waveform)));
    private Guid? selectedCue;
    private Guid? selectedTrack;
    private ProjectLayer? selectedLayer;
    private MediaTime position;
    private TimelineViewport viewport = new();
    private readonly HashSet<Guid> selectedIds = [];
    private readonly HashSet<Guid> collapsedTracks = [];
    private readonly HashSet<Guid> collapsedGroups = [];
    private readonly HashSet<Guid> collapsedAnimations = [];
    private IReadOnlyList<MediaTime> snapBoundaries = [];
    private MediaTime? snapTarget;
    private AnimationValue originalAnimationValue;
    private TimelineComponentMask dragComponents;
    private IReadOnlyList<TimelineKeyframeMarker> markers = [];
    private bool markersDirty = true;
    private TimelineHoverState? hover;
    private IReadOnlyList<TimelineRow> rows = [];
    private double duration = 60;
    private Guid? pendingTrackId;
    private Guid? originalTrackId;
    private bool validDrop = true;
    private double contentHeight;
    private TimelineDragMode dragMode;
    private Guid dragId;
    private double dragPointer;
    private double dragPointerY;
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
    private AnimationProperty dragProperty;
    private Rect dragCurve;
    private bool stretching;
    private AnimationProperty effectProperty = AnimationProperty.OPACITY;
    private IPointer? capturedPointer;
    private MediaTime? lastSeekRequest;

    /// <summary>创建可交互时间线。</summary>
    public SubtitleTimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        ActualThemeVariantChanged += (_, _) => RefreshTheme();
        RefreshTheme();
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
    public event EventHandler<TimelineTrackContextEventArgs>? TrackContextRequested;
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

    public bool IsSnapEnabled { get; set; } = true;
    public bool IsStepEnabled { get; set; }

    public static readonly StyledProperty<bool> IsSpectrumVisibleProperty =
        AvaloniaProperty.Register<SubtitleTimelineControl, bool>(nameof(IsSpectrumVisible), true);
    public static readonly StyledProperty<bool> IsWaveformVisibleProperty =
        AvaloniaProperty.Register<SubtitleTimelineControl, bool>(nameof(IsWaveformVisible), true);

    static SubtitleTimelineControl()
    {
        AffectsRender<SubtitleTimelineControl>(IsSpectrumVisibleProperty, IsWaveformVisibleProperty);
    }

    public bool IsSpectrumVisible
    {
        get => GetValue(IsSpectrumVisibleProperty);
        set => SetValue(IsSpectrumVisibleProperty, value);
    }

    public bool IsWaveformVisible
    {
        get => GetValue(IsWaveformVisibleProperty);
        set => SetValue(IsWaveformVisibleProperty, value);
    }

    internal MediaTime? SnapTarget => snapTarget;

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
            markersDirty = true;
            ClearHover();
            InvalidateVisual();
        }
    }

    /// <summary>替换显示快照，不持有可变编辑器。</summary>
    public void SetDocument(ProjectDocument value, Guid? cueId, ProjectLayer? layer, IReadOnlyList<Guid>? selection = null,
        Guid? trackId = null)
    {
        var nextSelection = selection ?? (layer is null ? Array.Empty<Guid>() : [layer.Id]);
        if (ReferenceEquals(document, value) && selectedCue == cueId && ReferenceEquals(selectedLayer, layer) &&
            selectedIds.SetEquals(nextSelection) && selectedTrack == trackId)
        {
            return;
        }

        var selectionChanged = selectedLayer?.Id != layer?.Id || selectedTrack != trackId;
        var previousProperties = selectedLayer is { } previousLayer ? GetAnimationProperties(previousLayer.Id) : [];
        if (!ReferenceEquals(document, value) || selectedCue != cueId || selectedLayer?.Id != layer?.Id)
        {
            CancelDrag();
        }

        var documentChanged = !ReferenceEquals(document, value);
        document = value;
        layersById = Flatten(value.Layers).ToDictionary(item => item.Id);
        cuesById = value.Subtitles.ToDictionary(item => item.Id);
        if (documentChanged)
        {
            valueRanges.Clear();
        }
        selectedCue = cueId;
        selectedLayer = layer;
        selectedTrack = trackId;
        selectedIds.Clear();
        selectedIds.UnionWith(nextSelection);
        RebuildRows();
        var animationRowsChanged = selectedLayer is { } nextLayer &&
            !previousProperties.SequenceEqual(GetAnimationProperties(nextLayer.Id));
        if (selectionChanged || animationRowsChanged)
        {
            EnsureSelectedRowVisible();
        }
        else
        {
            SetViewport(viewport, duration);
        }
        InvalidateVisual();
    }

    /// <summary>建立一次有界显示位图，高频率在顶端。</summary>
    public void SetSpectrogram(SpectrogramData? value)
    {
        spectrum = value;
        RebuildSpectrogramBitmap();
        InvalidateVisual();
    }

    /// <summary>替换独立颜色映射，不重新分析媒体或改变时间线视口。</summary>
    public void SetAudioGraphPalette(AudioGraphPalette value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        if (audioGraphPalette == value)
        {
            return;
        }

        audioGraphPalette = value;
        RefreshAudioGraph();
    }

    private void RefreshTheme()
    {
        drawingPalette = new(ActualThemeVariant == ThemeVariant.Dark);
        RefreshAudioGraph();
    }

    private void RefreshAudioGraph()
    {
        var effective = AudioGraphPalettes.Resolve(audioGraphPalette, ActualThemeVariant != ThemeVariant.Dark);
        spectrumColors = AudioGraphColorRamp.Create(effective);
        waveformPen = new(new SolidColorBrush(AudioGraphColorRamp.Parse(effective.Waveform)));
        RebuildSpectrogramBitmap();
        InvalidateVisual();
    }

    private unsafe void RebuildSpectrogramBitmap()
    {
        spectrumBitmap?.Dispose();
        spectrumBitmap = null;
        if (spectrum is { } value)
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
                    var color = spectrumColors[levels[(value.Height - 1 - y) * value.Width + x]];
                    var offset = x * 4;
                    row[offset] = color.B;
                    row[offset + 1] = color.G;
                    row[offset + 2] = color.R;
                    row[offset + 3] = 255;
                }
            }
        }

    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        var foreground = drawingPalette.Foreground;
        var grid = drawingPalette.Grid;
        context.DrawRectangle(drawingPalette.Surface, null, new(Bounds.Size));
        var body = new Rect(HeaderWidth, RulerHeight, Math.Max(0, Bounds.Width - HeaderWidth),
            Math.Max(0, Bounds.Height - RulerHeight));
        using (context.PushClip(body))
        {
            if (IsSpectrumVisible && spectrum is not null && spectrumBitmap is not null)
            {
                var length = Seconds(spectrum.Duration);
                var start = Math.Clamp(ViewStart / length * spectrum.Width, 0, spectrum.Width);
                var visible = Math.Min(spectrum.Width - start, VisibleDuration / length * spectrum.Width);
                if (visible > 0)
                {
                    context.DrawImage(spectrumBitmap, new Rect(start, 0, visible, spectrum.Height),
                        new Rect(HeaderWidth, body.Top, visible / spectrum.Width * length * PixelsPerSecond, body.Height));
                }

            }

            if (IsWaveformVisible && spectrum is not null)
            {
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
                if (row.CurveHeight > 0)
                {
                    context.DrawRectangle(drawingPalette.AnimationSurface, null,
                        new(HeaderWidth, y, Math.Max(0, Bounds.Width - HeaderWidth), row.CurveHeight));
                    foreach (var clip in ClipsForRow(row))
                    {
                        foreach (var animation in row.Animations)
                        {
                            if (CurveRectangle(clip.Id, animation.Property) is { } curve &&
                                curve.Bottom >= RulerHeight && curve.Top <= Bounds.Height)
                            {
                                DrawEffects(context, clip, animation.Property, curve);
                            }
                        }
                    }
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
                    context.DrawRectangle(invalid ? drawingPalette.InvalidClip : active ? drawingPalette.ActiveClip :
                            clip.Kind == LayerKind.SUBTITLE ? drawingPalette.SubtitleClip : drawingPalette.SceneClip,
                        new Pen(active ? drawingPalette.ActiveClipBorder : drawingPalette.ClipBorder, active ? 2 : 1), rectangle, 3, 3);
                    if (rectangle.Width > 8 && rectangle.Height >= 10)
                    {
                        var text = clip.SubtitleId is { } cueId ? cuesById[cueId].Text.Replace('\n', ' ') : clip.Name;
                        using (context.PushClip(rectangle.Deflate(3)))
                        {
                            DrawText(context, text, new(rectangle.X + 6, rectangle.Y + 3), drawingPalette.ClipForeground, 11);
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
                context.DrawRectangle(row.TrackId == selectedTrack && selectedTrack.HasValue
                        ? drawingPalette.SelectedTrack : drawingPalette.Track, null,
                    new(0, y, HeaderWidth, row.Height));
                DrawText(context, row.IsCollapsed ? "▸" : "▾", new(6 + row.Depth * 8, y + 5), foreground, 12);
                if (row.Clips.Any(clip => clip.Tracks.Any(track => !track.Keyframes.IsEmpty)))
                {
                    DrawText(context, row.CurveHeight > 0 ? "◆" : "◇", new(28 + row.Depth * 8, y + 5), foreground, 12);
                }

                using (context.PushClip(new Rect(46 + row.Depth * 8, y, Math.Max(0, HeaderWidth - 48 - row.Depth * 8), row.Height)))
                {
                    DrawText(context, row.Name, new(46 + row.Depth * 8, y + 5), foreground, 11);
                }
                if (row.StyleBadgeRectangle(y, HeaderWidth) is { } badge)
                {
                    using (context.PushOpacity(row.AutoApplyStyle ? 1 : 0.5))
                    {
                        context.DrawRectangle(drawingPalette.StyleBadge, null, badge, 3, 3);
                        using (context.PushClip(badge.Deflate(new Thickness(4, 0))))
                        {
                            DrawText(context, row.StylePresetName!, new(badge.X + 4, badge.Y + 2), foreground, 10);
                        }
                    }
                }
            }
        }

        context.DrawRectangle(drawingPalette.Surface, null,
            new(0, 0, Bounds.Width, RulerHeight));
        var step = TimelineTimeScale.MajorStep(PixelsPerSecond);
        var minor = Seconds(TimelineTimeScale.MinorStep(PixelsPerSecond));
        for (var index = Math.Ceiling(ViewStart / minor); index * minor <= ViewStart + VisibleDuration; index++)
        {
            var tick = index * minor;
            var x = X(tick);
            if (x >= HeaderWidth)
            {
                context.DrawLine(grid, new(x, RulerHeight - 3), new(x, RulerHeight));
            }
        }
        for (var index = Math.Ceiling(ViewStart / step); index * step <= ViewStart + VisibleDuration; index++)
        {
            var tick = index * step;
            var x = X(tick);
            if (x >= HeaderWidth)
            {
                context.DrawLine(grid, new(x, RulerHeight - 6), new(x, Bounds.Height));
                DrawText(context, TimelineTimeScale.Label(tick, step), new(x + 3, 2), foreground, 10);
            }
        }

        DrawPropertyTitles(context, foreground);
        DrawSnapIndicator(context);
        DrawKeyframeMarkers(context);

        var playhead = X(Seconds(position));
        if (playhead >= HeaderWidth)
        {
            context.DrawLine(drawingPalette.Playhead, new(playhead, 0), new(playhead, Bounds.Height));
        }

        if (ContentHeight > viewport.Height && viewport.Height > 0)
        {
            var thumbHeight = Math.Max(12, viewport.Height * viewport.Height / ContentHeight);
            var thumbY = RulerHeight + viewport.VerticalOffset / (ContentHeight - viewport.Height) * (viewport.Height - thumbHeight);
            context.DrawRectangle(new SolidColorBrush(Color.Parse("#998B9AB1")), null,
                new(Bounds.Width - 5, thumbY, 4, thumbHeight), 2, 2);
        }

        DrawHoveredLabel(context, foreground, dark);
    }

    private void DrawSnapIndicator(DrawingContext context)
    {
        if (!HasActiveDrag || !validDrop || snapTarget is not { } target)
        {
            return;
        }

        var x = X(Seconds(target));
        if (x < HeaderWidth || x > Bounds.Width)
        {
            return;
        }

        using var body = context.PushClip(BodyRectangle());
        context.DrawRectangle(drawingPalette.SnapFill, drawingPalette.SnapBorder,
            new(x - 2, RulerHeight + 1, 4, Math.Max(0, Bounds.Height - RulerHeight - 2)));
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            CancelDrag();
            TrackContextRequested?.Invoke(this, new(RowAt(e.GetPosition(this).Y)?.TrackId));
            e.Handled = true;
            return;
        }

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
                else if (point.X < 46 + row.Depth * 8 && row.Clips.Any(clip => clip.Tracks.Any(track => !track.Keyframes.IsEmpty)))
                {
                    Toggle(collapsedAnimations, row.Id);
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
        if (row is not null && FindKeyframe(point) is { } hit)
        {
            var selection = new TimelineKeyframeEventArgs(hit.Identity.LayerId, hit.Identity.Property, hit.Identity.Time,
                hit.Identity.Time, components: hit.Components);
            KeyframeSelected?.Invoke(this, selection);
            if (!selection.SelectionAccepted)
            {
                e.Handled = true;
                return;
            }

            var currentLayer = layersById[hit.Identity.LayerId];
            var currentTrack = currentLayer.Tracks.Single(item => item.Property == hit.Identity.Property);
            var currentKey = currentTrack.Keyframes.Single(item => item.Time == hit.Identity.Time);
            BeginKeyframeDrag(currentLayer, currentKey, point.X, hit.Identity.Property, point.Y, hit.Components);
            hover = new(point, hit);
            if (HasActiveDrag)
            {
                capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
            }
            e.Handled = true;
            return;
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
                    ? cuesById[cueId].TrackId : null;
                capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
                UpdateCursor(point);
                e.Handled = true;
                return;
            }
        }

        if (point.Y >= RulerHeight)
        {
            if (row?.TrackId is { } trackId && point.Y >= RowY(row) + row.CurveHeight)
            {
                TrackSelected?.Invoke(this, new(trackId));
                e.Handled = true;
            }

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
        var seconds = (point.X - dragPointer) / PixelsPerSecond;
        var delta = IsStepEnabled && (e.KeyModifiers & KeyModifiers.Alt) == 0
            ? new MediaTime((long)Math.Round(seconds * 1000000), 1000000)
            : TimeFromSeconds(seconds, e.KeyModifiers);
        var minimum = new MediaTime(document.FrameRate.Denominator, document.FrameRate.Numerator);
        snapTarget = null;
        switch (dragMode)
        {
            case TimelineDragMode.SEEK:
                RequestSeek(TimeAt(point.X, e.KeyModifiers));
                break;
            case TimelineDragMode.MOVE:
                pendingStart = Max(MediaTime.Zero, QuantizeEdit(originalStart + delta, e.KeyModifiers));
                pendingEnd = originalEnd + pendingStart - originalStart;
                if (ShouldSnap(e.KeyModifiers))
                {
                    var snap = TimelineQuantization.ResolveSnapOffset(pendingStart, pendingEnd, snapBoundaries, PixelsPerSecond);
                    pendingStart = Max(MediaTime.Zero, pendingStart + snap.Value);
                    pendingEnd = pendingStart + originalEnd - originalStart;
                    snapTarget = snap.Boundary == pendingStart || snap.Boundary == pendingEnd ? snap.Boundary : null;
                }
                break;
            case TimelineDragMode.TRIM_START:
                pendingStart = Max(MediaTime.Zero, Min(originalEnd - minimum, SnapEdit(originalStart + delta, e.KeyModifiers)));
                snapTarget = snapTarget == pendingStart ? snapTarget : null;
                break;
            case TimelineDragMode.TRIM_END:
                pendingEnd = Max(originalStart + minimum, SnapEdit(originalEnd + delta, e.KeyModifiers));
                snapTarget = snapTarget == pendingEnd ? snapTarget : null;
                break;
            case TimelineDragMode.KEYFRAME:
                var keyLayer = layersById[dragId];
                var sceneTime = keyLayer.Start + originalKey - keyLayer.AnimationOffset + delta;
                pendingKey = LayerAnimationTiming.ClampTime(keyLayer,
                    SnapEdit(sceneTime, e.KeyModifiers) - keyLayer.Start + keyLayer.AnimationOffset);
                snapTarget = snapTarget == keyLayer.Start + pendingKey - keyLayer.AnimationOffset ? snapTarget : null;
                pendingValue = Math.Clamp(originalValue + (dragPointerY - point.Y) / dragCurve.Height *
                    (valueMaximum - valueMinimum), valueMinimum, valueMaximum);
                break;
        }

        if (dragMode is TimelineDragMode.MOVE or TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END)
        {
            var previousTrack = pendingTrackId;
            UpdateTimingDrop(point.Y);
            if (!validDrop || snapTarget != pendingStart && snapTarget != pendingEnd)
            {
                snapTarget = null;
            }
            if (previousTrack != pendingTrackId)
            {
                RebuildRows();
            }
        }

        if (dragMode != TimelineDragMode.NONE)
        {
            InvalidateVisual();
        }

        if (dragMode is not (TimelineDragMode.NONE or TimelineDragMode.SEEK))
        {
            markersDirty = true;
        }
        UpdateHover(point);
        UpdateCursor(point);
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
        if (mode == TimelineDragMode.KEYFRAME && (pendingKey != originalKey || !pendingValue.Equals(originalValue)))
        {
            KeyframeMoved?.Invoke(this, new(dragId, dragProperty, originalKey, pendingKey,
                UpdatedAnimationValue(), dragComponents));
        }
        else if (mode is TimelineDragMode.MOVE or TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END &&
                 canCommit && (pendingStart != originalStart || pendingEnd != originalEnd || targetTrack != originalTrackId))
        {
            var editedLayer = layersById[dragId];
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
        CacheSnapBoundaries(id);
    }

    internal void BeginKeyframeDrag(ProjectLayer layer, Keyframe key, double pointer)
    {
        BeginKeyframeDrag(layer, key, pointer, layer.Tracks.First(track => track.Keyframes.Contains(key)).Property);
    }

    private void BeginKeyframeDrag(ProjectLayer layer, Keyframe key, double pointer, AnimationProperty property,
        double? pointerY = null, TimelineComponentMask components = TimelineComponentMask.FIRST)
    {
        CancelDrag();
        dragMode = TimelineDragMode.KEYFRAME;
        dragPointer = pointer;
        originalKey = pendingKey = key.Time;
        originalAnimationValue = key.Value;
        dragComponents = components;
        var firstComponent = FirstComponent(components);
        originalValue = pendingValue = key.Value.GetComponent(firstComponent);
        dragId = layer.Id;
        dragProperty = property;
        dragCurve = ComponentCurve(CurveRectangle(layer.Id, property)!.Value, key.Value, firstComponent);
        (valueMinimum, valueMaximum) = ComponentRange(key.Value, firstComponent,
            ValueRange(layer.Tracks.Single(track => track.Property == property)));
        dragPointerY = pointerY ?? ValueY(originalValue, valueMinimum, valueMaximum, dragCurve);
        CacheSnapBoundaries(layer.Id);
    }

    internal void CancelGesture()
    {
        ClearHover();
        CancelDrag();
    }

    private void CancelDrag()
    {
        var wasDragging = HasActiveDrag;
        snapTarget = null;
        dragMode = TimelineDragMode.NONE;
        markersDirty = true;
        ClearHover();
        Cursor = null;
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

    private void DrawEffects(DrawingContext context, ProjectLayer source, AnimationProperty property, Rect curve)
    {
        var layer = DisplayedLayer(source);
        var startX = Math.Max(HeaderWidth, X(Seconds(layer.Start)));
        var endX = Math.Min(Bounds.Width, X(Seconds(layer.End)));
        if (endX <= startX)
        {
            return;
        }
        var track = DisplayedTrack(layer, property);
        if (track is null)
        {
            return;
        }
        var sharedRange = dragMode == TimelineDragMode.KEYFRAME && dragId == layer.Id && dragProperty == property &&
            !(track.Keyframes[0].Value.IsColor && FirstComponent(dragComponents) == 3)
            ? (valueMinimum, valueMaximum) : CachedValueRange(source, track);
        using var clip = context.PushClip(new Rect(startX, curve.Top - 1, endX - startX, curve.Height + 2));
        for (var component = 0; component < track.Keyframes[0].Value.ComponentCount; component++)
        {
            var value = track.Keyframes[0].Value;
            var area = ComponentCurve(curve, value, component);
            var range = ComponentRange(value, component, sharedRange);
            var curvePen = new Pen(ComponentBrush(value, component), 2);
            if (value.IsColor && component == 3)
            {
                curvePen.DashStyle = DashStyle.Dash;
            }
            Point? previous = null;
            var samples = Math.Max(2, (int)(endX - startX) / 3);
            for (var sample = 0; sample <= samples; sample++)
            {
                var x = startX + sample * (endX - startX) / samples;
                var time = new MediaTime((long)Math.Round((ViewStart + (x - HeaderWidth) / PixelsPerSecond) * 1000000), 1000000) -
                    layer.Start + layer.AnimationOffset;
                var point = new Point(x, ValueY(SceneEvaluator.EvaluateTrack(track, time).GetComponent(component),
                    range.Minimum, range.Maximum, area));
                if (previous is { } first)
                {
                    context.DrawLine(curvePen, first, point);
                }
                previous = point;
            }
        }
    }

    private void DrawPropertyTitles(DrawingContext context, IBrush foreground)
    {
        using var body = context.PushClip(BodyRectangle());
        foreach (var row in rows)
        {
            foreach (var animation in row.Animations)
            {
                var top = RowY(row) + animation.Top;
                if (top + animation.Height < RulerHeight || top > Bounds.Height)
                {
                    continue;
                }
                var title = WorkbenchText.Property(animation.Property);
                var titleLayout = new FormattedText(title, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                    new Typeface(FontFamily.Default), 11, foreground);
                context.DrawRectangle(drawingPalette.Surface, null,
                    new(HeaderWidth + 2, top, titleLayout.Width + 6, titleLayout.Height + 3), 2, 2);
                context.DrawText(titleLayout, new(HeaderWidth + 5, top + 2));
                if (animation.Property is AnimationProperty.FILL or AnimationProperty.STROKE)
                {
                    DrawText(context, "R", new(HeaderWidth + 105, top + 2), drawingPalette.ColorComponents[0], 10);
                    DrawText(context, "G", new(HeaderWidth + 118, top + 2), drawingPalette.ColorComponents[1], 10);
                    DrawText(context, "B", new(HeaderWidth + 131, top + 2), drawingPalette.ColorComponents[2], 10);
                    DrawText(context, "A  0–1", new(HeaderWidth + 5, top + animation.Height - 44), foreground, 10);
                }
            }
        }
    }

    private void DrawKeyframeMarkers(DrawingContext context)
    {
        using var body = context.PushClip(BodyRectangle());
        foreach (var marker in GetMarkers())
        {
            var diamond = new StreamGeometry();
            var point = marker.Position;
            using (var geometry = diamond.Open())
            {
                geometry.BeginFigure(new(point.X, point.Y - 6));
                geometry.LineTo(new(point.X + 6, point.Y));
                geometry.LineTo(new(point.X, point.Y + 6));
                geometry.LineTo(new(point.X - 6, point.Y));
                geometry.EndFigure(true);
            }
            var brush = IsMultiple(marker.Components) ? new SolidColorBrush(Colors.Gold) :
                ComponentBrush(marker.Value, FirstComponent(marker.Components));
            context.DrawGeometry(brush, IsMultiple(marker.Components) ? new Pen(Brushes.White, 1.5) : drawingPalette.MarkerBorder, diamond);
        }
    }

    private void DrawHoveredLabel(DrawingContext context, IBrush foreground, bool dark)
    {
        if (hover is null)
        {
            return;
        }
        var marker = FindKeyframe(hover.Pointer);
        hover = hover with { Marker = marker };
        if (marker is null)
        {
            return;
        }
        using var body = context.PushClip(BodyRectangle());
        var text = LabelText(marker);
        var label = LabelRectangle(marker.Position, text);
        context.DrawRectangle(new SolidColorBrush(Color.Parse(dark ? "#F0202C40" : "#F0E7EDF5")),
            new Pen(new SolidColorBrush(Color.Parse(dark ? "#77869E" : "#9AA9BB"))), label, 3, 3);
        DrawText(context, text, label.TopLeft + new Vector(4, 2), foreground, 11);
    }

    internal IReadOnlyList<TimelineKeyframeMarker> KeyframeMarkers => GetMarkers();
    internal TimelineKeyframeMarker? HoveredKeyframe => hover?.Marker;

    private IReadOnlyList<TimelineKeyframeMarker> GetMarkers()
    {
        if (!markersDirty)
        {
            return markers;
        }
        var result = new List<TimelineKeyframeMarker>();
        foreach (var row in rows)
        {
            if (RowY(row) + row.CurveHeight < RulerHeight || RowY(row) > Bounds.Height)
            {
                continue;
            }
            foreach (var source in ClipsForRow(row))
            {
                var layer = DisplayedLayer(source);
                if (X(Seconds(layer.End)) < HeaderWidth - 6 || X(Seconds(layer.Start)) > Bounds.Width + 6)
                {
                    continue;
                }
                foreach (var animation in row.Animations)
                {
                    var track = DisplayedTrack(layer, animation.Property);
                    if (track is null || CurveRectangle(layer.Id, animation.Property) is not { } curve)
                    {
                        continue;
                    }
                    var range = dragMode == TimelineDragMode.KEYFRAME && dragId == layer.Id &&
                        dragProperty == animation.Property && !(track.Keyframes[0].Value.IsColor && FirstComponent(dragComponents) == 3)
                        ? (valueMinimum, valueMaximum) : CachedValueRange(source, track);
                    foreach (var key in track.Keyframes)
                    {
                        var x = X(Seconds(layer.Start + key.Time - layer.AnimationOffset));
                        if (x >= HeaderWidth - 9 && x <= Bounds.Width + 9)
                        {
                            result.AddRange(CreateMarkers(layer, track.Property, key, curve, range));
                        }
                    }
                }
            }
        }
        markers = result;
        markersDirty = false;
        return markers;
    }

    private List<TimelineKeyframeMarker> CreateMarkers(ProjectLayer layer, AnimationProperty property,
        Keyframe key, Rect curve, (double Minimum, double Maximum) range)
    {
        var x = X(Seconds(layer.Start + key.Time - layer.AnimationOffset));
        var result = new List<TimelineKeyframeMarker>();
        for (var component = 0; component < key.Value.ComponentCount; component++)
        {
            var area = ComponentCurve(curve, key.Value, component);
            var limits = ComponentRange(key.Value, component, range);
            var point = new Point(x, ValueY(key.Value.GetComponent(component), limits.Minimum, limits.Maximum, area));
            var mask = ComponentMask(component);
            var index = result.FindIndex(item => item.Curve == area &&
                Enumerable.Range(0, key.Value.ComponentCount).Where(axis => Contains(item.Components, axis))
                    .All(axis => Math.Abs(ValueY(key.Value.GetComponent(axis), limits.Minimum, limits.Maximum, area) - point.Y) <= 1));
            if (index >= 0)
            {
                var previous = result[index];
                var count = CountComponents(previous.Components);
                result[index] = previous with
                {
                    Components = previous.Components | mask,
                    Position = new(x, (previous.Position.Y * count + point.Y) / (count + 1))
                };
            }
            else
            {
                result.Add(new(new(layer.Id, property, key.Time), key.Value, mask, point, area,
                    limits.Minimum, limits.Maximum));
            }
        }
        return result;
    }

    private ProjectLayer DisplayedLayer(ProjectLayer layer)
    {
        if (dragId != layer.Id)
        {
            return layer;
        }
        if (dragMode == TimelineDragMode.MOVE)
        {
            return layer with { Start = pendingStart, End = pendingEnd };
        }
        return dragMode is TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END
            ? LayerAnimationTiming.Retime(layer, pendingStart, pendingEnd,
                stretching ? TimelineEditMode.STRETCH : TimelineEditMode.CROP) : layer;
    }

    private AnimationTrack? DisplayedTrack(ProjectLayer layer, AnimationProperty property)
    {
        var track = layer.Tracks.FirstOrDefault(item => item.Property == property);
        if (track is null || track.Keyframes.IsEmpty)
        {
            return null;
        }
        if (dragMode != TimelineDragMode.KEYFRAME || dragId != layer.Id || dragProperty != property)
        {
            return track;
        }
        return track with
        {
            Keyframes = track.Keyframes.Where(key => key.Time != pendingKey || key.Time == originalKey)
                .Select(key => key.Time == originalKey ? key with { Time = pendingKey, Value = UpdatedAnimationValue() } : key)
                .OrderBy(key => key.Time).ToImmutableArray()
        };
    }

    private AnimationValue UpdatedAnimationValue()
    {
        var delta = pendingValue - originalValue;
        var minimumDelta = double.NegativeInfinity;
        var maximumDelta = double.PositiveInfinity;
        for (var component = 0; component < originalAnimationValue.ComponentCount; component++)
        {
            if (!Contains(dragComponents, component))
            {
                continue;
            }
            var value = originalAnimationValue.GetComponent(component);
            minimumDelta = Math.Max(minimumDelta,
                Math.Max(valueMinimum, AnimationPropertyMetadata.GetMinimum(dragProperty, component)) - value);
            maximumDelta = Math.Min(maximumDelta,
                Math.Min(valueMaximum, AnimationPropertyMetadata.GetMaximum(dragProperty, component)) - value);
        }
        delta = Math.Clamp(delta, minimumDelta, maximumDelta);
        var edited = originalAnimationValue;
        for (var component = 0; component < edited.ComponentCount; component++)
        {
            if (Contains(dragComponents, component))
            {
                edited = edited.WithComponent(component, originalAnimationValue.GetComponent(component) + delta);
            }
        }
        return edited;
    }

    private static TimelineComponentMask ComponentMask(int component) => (TimelineComponentMask)(1 << component);
    private static bool Contains(TimelineComponentMask components, int component) => (components & ComponentMask(component)) != 0;
    private static bool IsMultiple(TimelineComponentMask components) => CountComponents(components) > 1;
    private static int CountComponents(TimelineComponentMask components) => System.Numerics.BitOperations.PopCount((uint)components);
    private static int FirstComponent(TimelineComponentMask components) => System.Numerics.BitOperations.TrailingZeroCount((uint)components);

    private static Rect ComponentCurve(Rect curve, AnimationValue value, int component)
    {
        if (!value.IsColor)
        {
            return curve;
        }
        var alphaHeight = Math.Min(22, Math.Max(8, curve.Height * 0.28));
        return component == 3
            ? new(curve.X, curve.Bottom - alphaHeight, curve.Width, alphaHeight)
            : new(curve.X, curve.Y, curve.Width, Math.Max(1, curve.Height - alphaHeight - 10));
    }

    private static (double Minimum, double Maximum) ComponentRange(AnimationValue value, int component,
        (double Minimum, double Maximum) shared)
    {
        return value.IsColor && component == 3 ? (0, 1) : shared;
    }

    private SolidColorBrush ComponentBrush(AnimationValue value, int component)
    {
        return value.IsColor ? drawingPalette.ColorComponents[component] : drawingPalette.Components[component];
    }

    private static (double Minimum, double Maximum) ValueRange(AnimationTrack track)
    {
        if (track.Property is AnimationProperty.OPACITY or AnimationProperty.FILL_ALPHA
            or AnimationProperty.STROKE_ALPHA or AnimationProperty.PATH_PROGRESS)
        {
            return (0, 1);
        }
        var values = track.Keyframes.SelectMany(frame => Enumerable.Range(0,
            frame.Value.IsColor ? 3 : frame.Value.ComponentCount).Select(frame.Value.GetComponent)).ToArray();
        var minimum = values.Min();
        var maximum = values.Max();
        if (track.Keyframes[0].Value.IsColor && minimum >= 0 && maximum <= 1)
        {
            return (0, 1);
        }
        var margin = Math.Max(track.Keyframes[0].Value.IsColor ? 0.1 : 1, (maximum - minimum) * 0.15);
        if (track.Property is AnimationProperty.BLUR or AnimationProperty.STROKE_WIDTH)
        {
            return (Math.Max(0, minimum - margin),
                Math.Min(track.Property == AnimationProperty.BLUR ? 512 : 4096, maximum + margin));
        }
        return (minimum - margin, maximum + margin);
    }

    private (double Minimum, double Maximum) CachedValueRange(ProjectLayer layer, AnimationTrack track)
    {
        var key = (layer.Id, track.Property);
        if (!valueRanges.TryGetValue(key, out var range))
        {
            range = ValueRange(layer.Tracks.Single(item => item.Property == track.Property));
            valueRanges.Add(key, range);
        }
        return range;
    }

    private Rect LabelRectangle(Point anchor, string text)
    {
        var formatted = new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface(FontFamily.Default), 11, Brushes.White);
        var body = BodyRectangle();
        var width = Math.Min(body.Width, formatted.Width + 8);
        var height = Math.Min(body.Height, formatted.Height + 4);
        var left = Math.Clamp(anchor.X + 12, body.Left, Math.Max(body.Left, body.Right - width));
        var top = Math.Clamp(anchor.Y + 6, body.Top, Math.Max(body.Top, body.Bottom - height));
        return new(left, top, width, height);
    }

    private static string LabelText(TimelineKeyframeMarker marker)
    {
        return string.Join(Environment.NewLine, Enumerable.Range(0, marker.Value.ComponentCount)
            .Where(component => Contains(marker.Components, component))
            .Select(component => AnimationPropertyMetadata.GetComponentName(marker.Identity.Property, component) + " " +
                marker.Value.GetComponent(component).ToString("0.###", CultureInfo.InvariantCulture)));
    }

    private Rect BodyRectangle() => new(HeaderWidth, RulerHeight, Math.Max(0, Bounds.Width - HeaderWidth),
        Math.Max(0, Bounds.Height - RulerHeight));

    private void UpdateHover(Point point)
    {
        var marker = BodyRectangle().Contains(point) ? FindKeyframe(point) : null;
        var next = marker is null ? null : new TimelineHoverState(point, marker);
        var changed = hover?.Marker != marker;
        hover = next;
        if (changed)
        {
            InvalidateVisual();
        }
    }

    private void ClearHover()
    {
        if (hover is not null)
        {
            hover = null;
            InvalidateVisual();
        }
    }

    private static double ValueY(double value, double minimum, double maximum, Rect curve)
    {
        return curve.Top + (1 - (value - minimum) / (maximum - minimum)) * curve.Height;
    }

    private void DrawWaveform(DrawingContext context, double top, double height)
    {
        var value = spectrum!;
        var data = value.Waveform.Span;
        var duration = Seconds(value.Duration);
        var center = top + height / 2;
        var first = Math.Clamp((int)(ViewStart / duration * value.Width), 0, value.Width - 1);
        var last = Math.Clamp((int)((ViewStart + VisibleDuration) / duration * value.Width), first, value.Width - 1);
        for (var index = first; index <= last; index++)
        {
            var x = X(index / (double)value.Width * duration);
            context.DrawLine(waveformPen, new(x, center - data[index * 2 + 1] * height * 0.4),
                new(x, center - data[index * 2] * height * 0.4));
        }
    }

    internal double ClipTop => selectedLayer is { } layer && GetClipRectangle(layer.Id) is { } rectangle
        ? rectangle.Top : RulerHeight;
    internal double ClipHeight => selectedLayer is { } layer && GetClipRectangle(layer.Id) is { } rectangle
        ? rectangle.Height : 24;
    internal double EffectTop => selectedLayer is { } layer && CurveRectangle(layer.Id, EffectProperty) is { } curve ? curve.Top : RulerHeight;
    internal double EffectHeight => selectedLayer is { } layer && CurveRectangle(layer.Id, EffectProperty) is { } curve ? curve.Height : 1;

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
    public Point? GetKeyframePoint(Guid layerId, MediaTime time, AnimationValue value)
    {
        return GetKeyframePoint(layerId, EffectProperty, time, value);
    }

    /// <summary>返回指定属性行中关键帧的真实坐标。</summary>
    public Point? GetKeyframePoint(Guid layerId, AnimationProperty property, MediaTime time, AnimationValue value,
        int? component = null)
    {
        var layer = layersById.GetValueOrDefault(layerId);
        var track = layer?.Tracks.FirstOrDefault(item => item.Property == property);
        if (layer is null || track is null || CurveRectangle(layerId, property) is not { } curve)
        {
            return null;
        }
        var range = CachedValueRange(layer, track);
        return CreateMarkers(layer, property, new(time, value), curve, range)
            .First(marker => Contains(marker.Components, component ?? 0)).Position;
    }

    /// <summary>返回当前悬停关键帧的标签范围；未悬停时返回 null。</summary>
    public Rect? GetKeyframeLabelRectangle(Guid layerId, AnimationProperty property, MediaTime time,
        int? component = null)
    {
        var marker = hover?.Marker;
        return marker?.Identity == new TimelineKeyframeIdentity(layerId, property, time) &&
            Contains(marker.Components, component ?? 0)
            ? LabelRectangle(marker.Position, LabelText(marker)) : null;
    }

    /// <summary>返回展开片段已有动画属性的显示顺序。</summary>
    public IReadOnlyList<AnimationProperty> GetAnimationProperties(Guid layerId)
    {
        var layer = layersById.GetValueOrDefault(layerId);
        var row = rowsByLayer.GetValueOrDefault(layerId);
        return row?.Animations.Where(animation => layer?.Tracks.Any(track =>
            track.Property == animation.Property && !track.Keyframes.IsEmpty) == true)
            .Select(animation => animation.Property).ToArray() ?? [];
    }

    /// <summary>返回真实轨道头部的可见坐标，供面板和辅助功能导航。</summary>
    public Rect? GetTrackHeaderRectangle(Guid trackId)
    {
        return rows.FirstOrDefault(row => row.TrackId == trackId) is { } value
            ? new Rect(0, RowY(value), HeaderWidth, value.Height) : null;
    }

    /// <summary>取得与轨道头绘制共用的样式预设徽章几何；点击此区域仍选择所属轨道。</summary>
    public Rect? GetTrackStyleBadgeRectangle(Guid trackId)
    {
        return rows.FirstOrDefault(row => row.TrackId == trackId) is { } row
            ? row.StyleBadgeRectangle(RowY(row), HeaderWidth) : null;
    }

    /// <summary>取得字幕轨道的紧凑显示状态。</summary>
    public bool IsTrackCollapsed(Guid trackId) => collapsedTracks.Contains(trackId);

    /// <summary>切换轨道的紧凑显示，不修改字幕或动画数据。</summary>
    public void ToggleTrackCollapse(Guid trackId)
    {
        if (!document.SubtitleTracks.Any(track => track.Id == trackId))
        {
            return;
        }

        CancelDrag();
        Toggle(collapsedTracks, trackId);
        RebuildRows();
        PublishViewport(viewport);
        InvalidateVisual();
    }

    private Rect? CurveRectangle(Guid layerId, AnimationProperty property)
    {
        var row = rowsByLayer.GetValueOrDefault(layerId);
        var animation = row?.Animations.FirstOrDefault(item => item.Property == property);
        var layer = layersById.GetValueOrDefault(layerId);
        return row is not null && animation is not null &&
            layer?.Tracks.Any(track => track.Property == property && !track.Keyframes.IsEmpty) == true
            ? new Rect(HeaderWidth, RowY(row) + animation.Top + 20,
                Math.Max(0, Bounds.Width - HeaderWidth), Math.Max(1, animation.Height - 34)) : null;
    }

    private void RebuildRows()
    {
        var result = new List<TimelineRow>();
        var flattened = layersById.Values.ToArray();
        var byCue = flattened.Where(layer => layer.SubtitleId.HasValue).ToDictionary(layer => layer.SubtitleId!.Value);
        var top = 0d;
        foreach (var track in document.SubtitleTracks)
        {
            var clips = document.Subtitles.Where(cue => cue.TrackId == track.Id).OrderBy(cue => cue.Start)
                .Select(cue => byCue[cue.Id]).ToArray();
            var collapsed = collapsedTracks.Contains(track.Id);
            var displayedClips = clips.Where(clip => dragMode != TimelineDragMode.MOVE ||
                clip.Id != dragId || pendingTrackId == track.Id).ToList();
            if (dragMode == TimelineDragMode.MOVE && originalTrackId.HasValue &&
                pendingTrackId == track.Id && originalTrackId != pendingTrackId)
            {
                displayedClips.Add(flattened.Single(layer => layer.Id == dragId));
            }
            var animations = !collapsed && !collapsedAnimations.Contains(track.Id)
                ? CreateAnimationRows(displayedClips) : [];
            var curve = animations.Sum(animation => animation.Height);
            var height = (track.StylePresetName is null ? 28 : 48) + curve;
            result.Add(new(track.Id, track.Id, track.Name, clips, 0, false, collapsed, top, height, animations,
                track.StylePresetName, track.AutoApplyStyle));
            top += height;
        }

        AddSceneRows(document.Layers, 0, result, ref top);
        rows = result;
        markersDirty = true;
        ClearHover();
        rowsByLayer = result.SelectMany(row => ClipsForRow(row).Select(clip => (clip.Id, Row: row)))
            .ToDictionary(value => value.Id, value => value.Row);
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
            var animations = !collapsedAnimations.Contains(layer.Id)
                ? CreateAnimationRows([layer]) : [];
            var curve = animations.Sum(animation => animation.Height);
            var height = 28 + curve;
            result.Add(new(layer.Id, null, layer.Name, [layer], depth, group, collapsed, top, height, animations));
            top += height;
            if (group && !collapsed)
            {
                AddSceneRows(layer.Children, depth + 1, result, ref top);
            }
        }
    }

    private static TimelineAnimationRow[] CreateAnimationRows(IEnumerable<ProjectLayer> clips)
    {
        var result = new List<TimelineAnimationRow>();
        var top = 0d;
        foreach (var property in clips.SelectMany(layer => layer.Tracks).Where(track => !track.Keyframes.IsEmpty)
                     .Select(track => track.Property).Distinct().OrderBy(property =>
                     {
                         var index = AnimationPropertyMetadata.CurrentProperties.IndexOf(property);
                         return index >= 0 ? index : AnimationPropertyMetadata.CurrentProperties.Length + (int)property;
                     }))
        {
            var height = property is AnimationProperty.FILL or AnimationProperty.STROKE ? 112 : 76;
            result.Add(new(property, top, height));
            top += height;
        }
        return result.ToArray();
    }

    private void UpdateCursor(Point point)
    {
        if (dragMode is TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END)
        {
            Cursor = resizeCursor;
            return;
        }

        if (dragMode != TimelineDragMode.NONE || point.X < HeaderWidth || point.Y < RulerHeight || RowAt(point.Y) is not { } row)
        {
            Cursor = null;
            return;
        }

        var clip = row.Clips.Reverse().FirstOrDefault(clip => clip.Kind != LayerKind.GROUP && ClipRectangle(clip, row).Contains(point));
        var rectangle = clip is null ? default : ClipRectangle(clip, row);
        Cursor = clip is not null && (point.X - rectangle.Left < 8 || rectangle.Right - point.X < 8)
            ? resizeCursor : null;
    }

    /// <inheritdoc />
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        ClearHover();
        if (!HasActiveDrag)
        {
            Cursor = null;
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
            yield return layersById[dragId];
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
        if (row.Top + row.Height <= offset)
        {
            offset = row.Top;
        }
        else if (row.Top >= offset + viewport.Height)
        {
            offset = row.Top;
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
        var layer = layersById[dragId];
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

    private TimelineKeyframeMarker? FindKeyframe(Point point)
    {
        if (!BodyRectangle().Contains(point))
        {
            return null;
        }
        TimelineKeyframeMarker? hit = null;
        var nearest = 81d;
        foreach (var marker in GetMarkers())
        {
            var offset = point - marker.Position;
            var distance = offset.X * offset.X + offset.Y * offset.Y;
            if (distance <= 81 && (hit is null || distance < nearest || distance.Equals(nearest) &&
                marker.Identity.LayerId == selectedLayer?.Id && hit.Identity.LayerId != selectedLayer?.Id))
            {
                hit = marker;
                nearest = distance;
            }
        }
        return hit;
    }

    private void CacheSnapBoundaries(Guid excludedLayerId)
    {
        snapBoundaries = layersById.Values.Where(layer => layer.Id != excludedLayerId)
            .SelectMany(layer => new[] { layer.Start, layer.End }).Distinct().Order().ToArray();
    }

    private bool ShouldSnap(KeyModifiers modifiers) => IsSnapEnabled && (modifiers & KeyModifiers.Alt) == 0;

    private MediaTime QuantizeEdit(MediaTime value, KeyModifiers modifiers)
    {
        return IsStepEnabled && (modifiers & KeyModifiers.Alt) == 0
            ? TimelineQuantization.Quantize(value, TimelineTimeScale.MinorStep(PixelsPerSecond)) : value;
    }

    private MediaTime SnapEdit(MediaTime value, KeyModifiers modifiers)
    {
        var result = QuantizeEdit(value, modifiers);
        if (!ShouldSnap(modifiers))
        {
            return result;
        }

        var snap = TimelineQuantization.ResolveSnap(result, snapBoundaries, PixelsPerSecond);
        snapTarget = snap.Boundary;
        return snap.Value;
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
