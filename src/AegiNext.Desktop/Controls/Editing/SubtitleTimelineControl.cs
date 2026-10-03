using System.Globalization;
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
    private double viewStart;
    private double pixelsPerSecond = 48;
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
    private bool showEffects;
    private IPointer? capturedPointer;
    private MediaTime? lastSeekRequest;

    /// <summary>创建可交互时间线。</summary>
    public SubtitleTimelineControl()
    {
        Focusable = true;
        ClipToBounds = true;
        MinHeight = 76;
        ActualThemeVariantChanged += (_, _) => InvalidateVisual();
    }

    public event EventHandler<TimelineSeekEventArgs>? SeekRequested;
    public event EventHandler<TimelineSelectionEventArgs>? CueSelected;
    public event EventHandler<TimelineTimingEventArgs>? TimingChanged;
    public event EventHandler<TimelineKeyframeEventArgs>? KeyframeMoved;
    public event EventHandler<TimelineKeyframeEventArgs>? KeyframeSelected;

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

    public bool ShowEffects
    {
        get => showEffects;
        set
        {
            if (showEffects != value)
            {
                CancelDrag();
                showEffects = value;
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
        get => viewStart;
        set
        {
            var next = Math.Max(0, value);
            if (viewStart.Equals(next))
            {
                return;
            }

            viewStart = next;
            InvalidateVisual();
        }
    }

    public double PixelsPerSecond
    {
        get => pixelsPerSecond;
        set
        {
            var next = Math.Clamp(value, 4, 500);
            if (pixelsPerSecond.Equals(next))
            {
                return;
            }

            pixelsPerSecond = next;
            InvalidateVisual();
        }
    }

    public double VisibleDuration => Bounds.Width / pixelsPerSecond;

    /// <summary>替换显示快照，不持有可变编辑器。</summary>
    public void SetDocument(ProjectDocument value, Guid? cueId, ProjectLayer? layer)
    {
        if (ReferenceEquals(document, value) && selectedCue == cueId && ReferenceEquals(selectedLayer, layer))
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
        var foreground = new SolidColorBrush(dark ? Color.Parse("#DDE3EE") : Color.Parse("#263247"));
        var grid = new Pen(new SolidColorBrush(dark ? Color.Parse("#334155") : Color.Parse("#CCD5E2")));
        context.DrawRectangle(new SolidColorBrush(dark ? Color.Parse("#131A26") : Color.Parse("#E7EDF5")), null,
            new Rect(Bounds.Size));
        var top = 28.0;
        var height = Math.Max(1, Bounds.Height - top - 8);
        if (spectrum is not null && spectrumBitmap is not null)
        {
            var duration = Seconds(spectrum.Duration);
            var start = Math.Clamp(viewStart / duration * spectrum.Width, 0, spectrum.Width);
            var visible = Math.Min(spectrum.Width - start, VisibleDuration / duration * spectrum.Width);
            if (visible > 0)
            {
                context.DrawImage(spectrumBitmap, new Rect(start, 0, visible, spectrum.Height),
                    new Rect(0, top, visible / spectrum.Width * duration * pixelsPerSecond, height));
            }

            DrawWaveform(context, top, height);
        }

        var step = pixelsPerSecond >= 100 ? 1 : pixelsPerSecond >= 30 ? 5 : 10;
        var tick = Math.Ceiling(viewStart / step) * step;
        for (; tick <= viewStart + VisibleDuration; tick += step)
        {
            var x = X(tick);
            context.DrawLine(grid, new(x, top), new(x, Bounds.Height));
            DrawText(context, TimeSpan.FromSeconds(tick).ToString(@"mm\:ss", CultureInfo.InvariantCulture),
                new(x + 4, 4), foreground, 11);
        }

        if (ShowEffects && selectedLayer is not null)
        {
            DrawEffects(context, foreground, top);
        }
        else
        {
            for (var index = 0; index < document.Subtitles.Length; index++)
            {
                var cue = document.Subtitles[index];
                var start = cue.Start;
                var end = cue.End;
                if (dragMode is TimelineDragMode.MOVE or TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END &&
                    dragId == cue.Id)
                {
                    start = pendingStart;
                    end = pendingEnd;
                }

                var rectangle = CueRectangle(start, end, index);
                if (rectangle.Right < 0 || rectangle.Left > Bounds.Width)
                {
                    continue;
                }

                var active = selectedCue == cue.Id;
                context.DrawRectangle(new SolidColorBrush(active ? Color.Parse("#A84568DC") : Color.Parse("#864FB3CB")),
                    new Pen(new SolidColorBrush(active ? Color.Parse("#A5B8FF") : Color.Parse("#77D4DF")),
                        active ? 2 : 1), rectangle, 4, 4);
                if (rectangle.Width > 8)
                {
                    using (context.PushClip(rectangle.Deflate(3)))
                    {
                        DrawText(context, $"{index + 1}  {cue.Text.Replace('\n', ' ')}",
                            new(rectangle.X + 6, rectangle.Y + 3), Brushes.White, 11);
                    }
                }
            }
        }

        var playhead = X(Seconds(position));
        context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#FF6B7A")), 2), new(playhead, 0),
            new(playhead, Bounds.Height));
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
        dragPointer = point.X;
        stretching = (e.KeyModifiers & KeyModifiers.Control) != 0;
        if (ShowEffects && selectedLayer is { } layer)
        {
            var track = layer.Tracks.FirstOrDefault(item => item.Property == EffectProperty);
            if (track is not null)
            {
                (valueMinimum, valueMaximum) = ValueRange(track);
                foreach (var key in track.Keyframes)
                {
                    var x = X(Seconds(layer.Start + key.Time - layer.AnimationOffset));
                    if (Math.Abs(point.X - x) <= 9 &&
                        Math.Abs(point.Y - ValueY(key.Value, valueMinimum, valueMaximum)) <= 12)
                    {
                        BeginKeyframeDrag(layer, key, point.X);
                        KeyframeSelected?.Invoke(this, new(layer.Id, EffectProperty, key.Time, key.Time));
                        if (!HasActiveDrag)
                        {
                            return;
                        }

                        capturedPointer = e.Pointer;
                        e.Pointer.Capture(this);
                        e.Handled = true;
                        return;
                    }
                }
            }
        }
        else
        {
            for (var index = document.Subtitles.Length - 1; index >= 0; index--)
            {
                var cue = document.Subtitles[index];
                var rectangle = CueRectangle(cue.Start, cue.End, index);
                if (!rectangle.Contains(point))
                {
                    continue;
                }

                var mode = point.X - rectangle.Left < 8 ? TimelineDragMode.TRIM_START :
                    rectangle.Right - point.X < 8 ? TimelineDragMode.TRIM_END : TimelineDragMode.MOVE;
                var initialDocument = document;
                CueSelected?.Invoke(this, new(cue.Id));
                if (!ReferenceEquals(initialDocument, document))
                {
                    return;
                }

                BeginTimingDrag(cue, mode, point.X, stretching);
                capturedPointer = e.Pointer;
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        dragMode = TimelineDragMode.SEEK;
        lastSeekRequest = null;
        RequestSeek(TimeAt(point.X, e.KeyModifiers));
        if (!HasActiveDrag)
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
        var point = e.GetPosition(this);
        var delta = TimeFromSeconds((point.X - dragPointer) / pixelsPerSecond, e.KeyModifiers);
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
                pendingKey = Max(MediaTime.Zero, originalKey + delta);
                pendingValue =
                    Math.Clamp(
                        valueMinimum + (1 - (point.Y - 52) / Math.Max(1, Bounds.Height - 76)) *
                        (valueMaximum - valueMinimum), valueMinimum, valueMaximum);
                break;
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

        CancelDrag();
        if (mode == TimelineDragMode.KEYFRAME && (pendingKey != originalKey || !pendingValue.Equals(originalValue)) &&
            selectedLayer is { } layer)
        {
            KeyframeMoved?.Invoke(this, new(layer.Id, EffectProperty, originalKey, pendingKey, pendingValue));
        }
        else if (mode is TimelineDragMode.MOVE or TimelineDragMode.TRIM_START or TimelineDragMode.TRIM_END &&
                 (pendingStart != originalStart || pendingEnd != originalEnd))
        {
            TimingChanged?.Invoke(this, new(dragId, originalStart, pendingStart, pendingEnd,
                stretching ? TimelineEditMode.STRETCH : TimelineEditMode.CROP, mode == TimelineDragMode.MOVE));
        }

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
        CancelDrag();
        dragMode = mode;
        dragId = cue.Id;
        dragPointer = pointer;
        stretching = stretch;
        originalStart = pendingStart = cue.Start;
        originalEnd = pendingEnd = cue.End;
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
        DrawText(context, WorkbenchText.Property(EffectProperty), new(8, top + 4), foreground, 12);
        var track = layer.Tracks.FirstOrDefault(item => item.Property == EffectProperty);
        if (track is null)
        {
            return;
        }

        var (minimum, maximum) =
            dragMode == TimelineDragMode.KEYFRAME ? (valueMinimum, valueMaximum) : ValueRange(track);
        var curvePen = new Pen(new SolidColorBrush(Color.Parse("#CCD4FF")), 2);
        Point? previous = null;
        var samples = Math.Max(2, (int)Bounds.Width / 3);
        for (var sample = 0; sample <= samples; sample++)
        {
            var x = sample * Bounds.Width / samples;
            var time = new MediaTime((long)Math.Round((viewStart + x / pixelsPerSecond) * 1000000), 1000000) -
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
            using (var geometry = diamond.Open())
            {
                geometry.BeginFigure(new(x, y - 7));
                geometry.LineTo(new(x + 7, y));
                geometry.LineTo(new(x, y + 7));
                geometry.LineTo(new(x - 7, y));
                geometry.EndFigure(true);
            }

            context.DrawGeometry(new SolidColorBrush(Color.Parse("#B7C6FF")), new Pen(Brushes.White), diamond);
            DrawText(context, value.ToString("0.##", CultureInfo.InvariantCulture), new(x + 8, y + 10), foreground, 11);
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
        return 52 + (1 - (value - minimum) / (maximum - minimum)) * Math.Max(1, Bounds.Height - 76);
    }

    private void DrawWaveform(DrawingContext context, double top, double height)
    {
        var value = spectrum!;
        var data = value.Waveform.Span;
        var duration = Seconds(value.Duration);
        var pen = new Pen(new SolidColorBrush(Color.Parse("#6079D5DB")));
        var center = top + height / 2;
        var first = Math.Clamp((int)(viewStart / duration * value.Width), 0, value.Width - 1);
        var last = Math.Clamp((int)((viewStart + VisibleDuration) / duration * value.Width), first, value.Width - 1);
        for (var index = first; index <= last; index++)
        {
            var x = X(index / (double)value.Width * duration);
            context.DrawLine(pen, new(x, center - data[index * 2 + 1] * height * 0.4),
                new(x, center - data[index * 2] * height * 0.4));
        }
    }

    private Rect CueRectangle(MediaTime start, MediaTime end, int index)
    {
        return new(X(Seconds(start)), Math.Max(32, Bounds.Height - 82) + index % 3 * 24,
            Math.Max(3, Seconds(end - start) * pixelsPerSecond), 21);
    }

    private MediaTime TimeAt(double x, KeyModifiers modifiers) =>
        Max(MediaTime.Zero, TimeFromSeconds(viewStart + x / pixelsPerSecond, modifiers));

    private MediaTime TimeFromSeconds(double value, KeyModifiers modifiers)
    {
        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            return new((long)Math.Round(value * 1000), 1000);
        }

        var frame = (long)Math.Round(value * document.FrameRate.Numerator / document.FrameRate.Denominator);
        return new(checked(frame * document.FrameRate.Denominator), document.FrameRate.Numerator);
    }

    private double X(double time) => (time - viewStart) * pixelsPerSecond;

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
