using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Styling;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Controls;

/// <summary>共用同一时间投影绘制、命中和拖拽的逐字轴；释放时提交一次时长或句前留白请求。</summary>
public sealed class KaraokeClipAxis : Control
{
    /// <summary>可绑定的吸附开关元数据，使用 Avalonia 的标准属性命名。</summary>
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Avalonia styled property metadata uses the public NameProperty convention.")]
    public static readonly StyledProperty<bool> IsSnapEnabledProperty =
        AvaloniaProperty.Register<KaraokeClipAxis, bool>(nameof(IsSnapEnabled), true);
    /// <summary>控制非拖拽期间是否常驻显示片段时长。</summary>
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "Avalonia styled property metadata uses the public NameProperty convention.")]
    public static readonly StyledProperty<bool> KeepDurationLabelsVisibleProperty =
        AvaloniaProperty.Register<KaraokeClipAxis, bool>(nameof(KeepDurationLabelsVisible));
    private SubtitleLine? line;
    private MediaTime offset;
    private Guid? selectedId;
    private Guid? draggingId;
    private int draggingIndex = -1;
    private SubtitleLine? frozen;
    private double pointerStart;
    private double frozenPixelsPerSecond;
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

    /// <summary>创建可捕获本地指针的卡拉 OK 编辑轴。</summary>
    public KaraokeClipAxis()
    {
        MinHeight = 88;
        Height = 88;
        ClipToBounds = true;
        Focusable = true;
    }

    public event EventHandler<KaraokeClipSelectionEventArgs>? ClipSelectionRequested;
    public event EventHandler<KaraokeClipDurationEventArgs>? DurationRequested;
    /// <summary>释放首字左柄时请求平移整组字时间，保留各字时长及字幕边界。</summary>
    public event EventHandler<KaraokeLeadingDelayEventArgs>? LeadingDelayRequested;
    private void RequestLeadingDelay(Guid subtitleId, MediaTime delay)
    {
        LeadingDelayRequested?.Invoke(this, new(subtitleId, delay));
    }
    /// <summary>单击片段请求弹出属性；拖拽保持一次时长事务，不打开编辑弹层。</summary>
    public event EventHandler<KaraokeClipEditRequestedEventArgs>? ClipEditRequested;
    /// <summary>启用右端拖拽的十毫秒网格和邻近时间边界吸附；Alt 暂时绕过。</summary>
    public bool IsSnapEnabled
    {
        get => GetValue(IsSnapEnabledProperty);
        set => SetValue(IsSnapEnabledProperty, value);
    }
    /// <summary>常驻显示片段的实际时长；关闭后仅在时长拖拽期间显示。</summary>
    public bool KeepDurationLabelsVisible
    {
        get => GetValue(KeepDurationLabelsVisibleProperty);
        set => SetValue(KeepDurationLabelsVisibleProperty, value);
    }
    /// <summary>当前字轴是否超过字幕结束边界；越界只影响提示与最终裁剪。</summary>
    public bool HasOverflow => line is not null && line.Karaoke.Select((clip, index) =>
        ToSeconds(PreviewEnd(clip, index) - offset)).Any(end => end > Seconds);
    private double Seconds => line is null ? 1 : (double)(line.End - line.Start).Numerator / (line.End - line.Start).Denominator;
    private double ViewSeconds => line is null || line.Karaoke.IsEmpty ? Seconds :
        Math.Max(Seconds, line.Karaoke.Max(clip => ToSeconds(clip.End - offset)));
    private double PixelsPerSecond => draggingId is not null ? frozenPixelsPerSecond :
        Math.Max(1, Bounds.Width - 24) / Math.Max(0.001, ViewSeconds);
    private static double ToSeconds(MediaTime value) => (double)value.Numerator / value.Denominator;

    /// <summary>同步数据；替换目标、撤销和外部字幕数据变化取消未完成的拖拽。</summary>
    public void SetContent(SubtitleLine? value, MediaTime animationOffset, Guid? selectedClipId)
    {
        if (value != line || animationOffset != offset)
        {
            CancelGesture();
        }
        line = value;
        offset = animationOffset;
        selectedId = selectedClipId;
        UpdateDurationLabels();
        InvalidateVisual();
    }

    /// <summary>丢弃尚未释放的手势，不发出业务请求。</summary>
    public void CancelGesture()
    {
        var pointer = capturedPointer;
        capturedPointer = null;
        draggingId = null;
        draggingIndex = -1;
        frozen = null;
        gestureKind = null;
        delta = MediaTime.Zero;
        snapTarget = null;
        snapBoundaries = [];
        pointer?.Capture(null);
        UpdateDurationLabels();
        InvalidateVisual();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KeepDurationLabelsVisibleProperty || change.Property == BoundsProperty || change.Property == IsVisibleProperty)
        {
            UpdateDurationLabels();
        }
    }

    private Rect Rectangle(KaraokeSegment clip, int index)
    {
        var start = clip.Start;
        var end = clip.End;
        if (draggingIndex >= 0)
        {
            if (gestureKind == KaraokeAxisGestureKind.LEADING_DELAY || index > draggingIndex)
            {
                start += delta;
                end += delta;
            }
            else if (index == draggingIndex)
            {
                end += delta;
            }
        }
        return new(12 + ToSeconds(start - offset) * PixelsPerSecond, 22,
            Math.Max(1, ToSeconds(end - start) * PixelsPerSecond), 42);
    }

    private MediaTime PreviewEnd(KaraokeSegment clip, int index)
    {
        return draggingIndex >= 0 && index >= draggingIndex ? clip.End + delta : clip.End;
    }

    /// <inheritdoc />
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(22, 120, 120, 120)), null, new(12, 22, Math.Max(1, Bounds.Width - 24), 42));
        if (line is null)
        {
            return;
        }
        var cueEnd = 12 + Seconds * PixelsPerSecond;
        if (HasOverflow && cueEnd < Bounds.Width - 12)
        {
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(30, 240, 50, 50)), null,
                new(cueEnd, 22, Bounds.Width - 12 - cueEnd, 42));
        }
        for (var index = 0; index < line.Karaoke.Length; index++)
        {
            var clip = line.Karaoke[index];
            var rect = Rectangle(clip, index);
            var selected = clip.Id == selectedId;
            context.DrawRectangle(new SolidColorBrush(selected ? Color.Parse("#496CA9") : Color.Parse("#58717D")),
                new Pen(selected ? Brushes.DodgerBlue : Brushes.Gray, selected ? 2 : 1), rect);
            using var text = WorkbenchTextFormatting.CreateLayout(this, line.Text.Substring(clip.Utf16Start, clip.Utf16Length), 14,
                Brushes.White, maximumWidth: Math.Max(1, rect.Width - 8));
            using (context.PushClip(rect))
            {
                text.Draw(context, WorkbenchTextFormatting.CenteredOrigin(text, new(rect.X + 4, rect.Y, Math.Max(1, rect.Width - 8), rect.Height)));
            }
            context.DrawLine(new Pen(Brushes.White), new(rect.Right - 3, rect.Top + 10), new(rect.Right - 3, rect.Bottom - 10));
            if (index == 0)
            {
                context.DrawLine(new Pen(Brushes.White), new(rect.Left + 3, rect.Top + 10), new(rect.Left + 3, rect.Bottom - 10));
            }
        }
        if (HasOverflow)
        {
            context.DrawLine(new Pen(Brushes.IndianRed, 2), new(cueEnd, 18), new(cueEnd, 64));
            context.DrawLine(new Pen(Brushes.IndianRed, 3), new(Bounds.Width - 12, 20), new(Bounds.Width - 12, 66));
        }
        if (IsSnapEnabled && snapTarget is { } snapped)
        {
            var x = 12 + ToSeconds(snapped - offset) * PixelsPerSecond;
            context.DrawRectangle(new SolidColorBrush(Color.FromArgb(64, 30, 144, 255)), new Pen(Brushes.DodgerBlue),
                new(x - 2, 16, 4, 50));
        }
        using var startLabel = WorkbenchTextFormatting.CreateLayout(this, "0", 11, Brushes.Gray);
        startLabel.Draw(context, new(12, 66));
        using var endLabel = WorkbenchTextFormatting.CreateLayout(this, ViewSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s", 11, Brushes.Gray);
        endLabel.Draw(context, new(Math.Max(12, Bounds.Width - endLabel.Width - 12), 66));
        if (HasOverflow && cueEnd < Bounds.Width - endLabel.Width - 24)
        {
            using var cueLabel = WorkbenchTextFormatting.CreateLayout(this,
                Seconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + " s", 11, Brushes.IndianRed);
            cueLabel.Draw(context, new(cueEnd, 66));
        }
    }

    /// <inheritdoc />
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (line is null || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }
        var point = e.GetPosition(this);
        var first = line.Karaoke.IsEmpty ? default : Rectangle(line.Karaoke[0], 0);
        var leadingHandle = !line.Karaoke.IsEmpty && new Rect(first.Left - 6, first.Top, 12, first.Height).Contains(point);
        for (var index = 0; index < line.Karaoke.Length; index++)
        {
            var clip = line.Karaoke[index];
            if (!Rectangle(clip, index).Contains(point) && !(index == 0 && leadingHandle))
            {
                continue;
            }
            ClipSelectionRequested?.Invoke(this, new(clip.Id));
            if (selectedId != clip.Id)
            {
                return;
            }
            Focus();
            frozenPixelsPerSecond = PixelsPerSecond;
            draggingId = clip.Id;
            draggingIndex = index;
            frozen = line;
            gestureKind = index == 0 && leadingHandle ? KaraokeAxisGestureKind.LEADING_DELAY : KaraokeAxisGestureKind.DURATION;
            snapBoundaries = gestureKind == KaraokeAxisGestureKind.LEADING_DELAY
                ? [offset, offset + line.End - line.Start]
                : line.Karaoke.SelectMany(item => new[] { item.Start, item.End })
                    .Append(offset + line.End - line.Start).Where(boundary => boundary > clip.Start).Distinct().Order().ToArray();
            pointerStart = PointerX(e);
            pointerMoved = false;
            delta = MediaTime.Zero;
            capturedPointer = e.Pointer;
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }
    }

    /// <inheritdoc />
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (draggingId is not null)
        {
            var pointerDelta = PointerX(e) - pointerStart;
            pointerMoved |= Math.Abs(pointerDelta) >= 4;
            var ticks = checked((long)Math.Round(pointerDelta / frozenPixelsPerSecond * TimeSpan.TicksPerSecond));
            delta = new(ticks, TimeSpan.TicksPerSecond);
            snapTarget = null;
            if (frozen is not null && draggingIndex >= 0)
            {
                var clip = frozen.Karaoke[draggingIndex];
                var leading = gestureKind == KaraokeAxisGestureKind.LEADING_DELAY;
                if (IsSnapEnabled && (e.KeyModifiers & KeyModifiers.Alt) == 0)
                {
                    var edge = leading ? clip.Start : clip.End;
                    var value = leading
                        ? offset + TimelineQuantization.Quantize(edge + delta - offset, new(1, 100))
                        : TimelineQuantization.Quantize(edge + delta, new(1, 100));
                    var snap = TimelineQuantization.ResolveSnap(value, snapBoundaries, frozenPixelsPerSecond);
                    if (leading || snap.Value > clip.Start)
                    {
                        delta = snap.Value - edge;
                        snapTarget = snap.Value;
                    }
                }
                if (leading)
                {
                    var minimum = offset > MediaTime.Zero ? offset : MediaTime.Zero;
                    if (clip.Start + delta < minimum)
                    {
                        delta = minimum - clip.Start;
                        snapTarget = null;
                    }
                }
                else
                {
                    var duration = clip.End - clip.Start;
                    var minimum = new MediaTime(1, TimeSpan.TicksPerSecond);
                    minimum = duration < minimum ? duration : minimum;
                    if (duration + delta < minimum)
                    {
                        delta = minimum - duration;
                        snapTarget = null;
                    }
                }
            }
            UpdateDurationLabels();
            InvalidateVisual();
        }
    }

    /// <inheritdoc />
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (draggingId is { } id && frozen is { } source && source.Karaoke.FirstOrDefault(value => value.Id == id) is { } clip)
        {
            var duration = clip.End - clip.Start + delta;
            var delay = clip.Start + delta - offset;
            var leading = gestureKind == KaraokeAxisGestureKind.LEADING_DELAY;
            var changed = delta != MediaTime.Zero;
            var editing = !pointerMoved;
            var anchor = Rectangle(clip, draggingIndex);
            CancelGesture();
            if (leading && !editing && changed)
            {
                RequestLeadingDelay(source.Id, delay);
            }
            else if (editing)
            {
                ClipEditRequested?.Invoke(this, new(source.Id, id, anchor));
            }
            else if (!leading && changed)
            {
                DurationRequested?.Invoke(this, new(source.Id, id, duration));
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

    private void UpdateDurationLabels()
    {
        var source = frozen ?? line;
        var editingDuration = pointerMoved && gestureKind == KaraokeAxisGestureKind.DURATION && frozen is not null;
        if (!IsVisible || visibilityAncestors.Any(ancestor => !ancestor.IsVisible) || Bounds.Width <= 0 || source is null ||
            !KeepDurationLabelsVisible && !editingDuration)
        {
            DurationLabels = [];
            durationLabelsAdorner.SetLabels(DurationLabels);
            return;
        }
        var candidates = new List<KaraokeDurationLabel>();
        for (var index = 0; index < source.Karaoke.Length; index++)
        {
            var clip = source.Karaoke[index];
            var duration = clip.End - clip.Start + (gestureKind == KaraokeAxisGestureKind.DURATION && index == draggingIndex ? delta : MediaTime.Zero);
            var value = ((decimal)duration.Numerator / duration.Denominator).ToString("0.#######", System.Globalization.CultureInfo.InvariantCulture) + " s";
            using var text = WorkbenchTextFormatting.CreateLayout(this, value, 11, Brushes.White, lineHeight: 20);
            var rect = Rectangle(clip, index);
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
