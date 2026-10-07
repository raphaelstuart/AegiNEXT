using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private const double COLLAPSED_ANIMATION_ROW_HEIGHT = 40;
    private readonly HashSet<TimelineAnimationRowId> collapsedAnimationRows = [];

    public static readonly StyledProperty<TimelineViewState> TimelineViewStateProperty =
        AvaloniaProperty.Register<SubtitleTimelineControl, TimelineViewState>(nameof(TimelineViewState), new());

    public TimelineViewState TimelineViewState
    {
        get => GetValue(TimelineViewStateProperty);
        set
        {
            value.Validate();
            SetValue(TimelineViewStateProperty, value);
        }
    }

    public event EventHandler<TimelineAnimationRowCollapseEventArgs>? AnimationRowCollapseRequested;

    /// <summary>右键命中关键帧或曲线时提交片段与属性身份，其余属性行区域提交整行身份。</summary>
    public event EventHandler<TimelineAnimationRowContextEventArgs>? AnimationRowContextRequested;

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsSpectrumVisibleProperty || change.Property == IsWaveformVisibleProperty)
        {
            audioDrawing.Dispose();
        }
        if (change.Property != TimelineViewStateProperty)
        {
            return;
        }

        var state = TimelineViewState;
        state.Validate();
        if (collapsedAnimationRows.SetEquals(state.CollapsedAnimationRows))
        {
            return;
        }

        CancelDrag();
        collapsedAnimationRows.Clear();
        collapsedAnimationRows.UnionWith(state.CollapsedAnimationRows);
        RebuildRows();
        PublishViewport(viewport, false);
        InvalidateVisual();
    }

    /// <summary>取得属性行的完整几何；整体折叠或不存在的属性行返回 null。</summary>
    public Rect? GetAnimationRowRectangle(TimelineAnimationRowId id)
    {
        var (owner, animation) = FindAnimationRow(id);
        return owner is not null && animation is not null
            ? animation.Rectangle(RowY(owner), HeaderWidth, Bounds.Width) : null;
    }

    /// <summary>取得与属性行绘制和鼠标命中共用的折叠按钮几何。</summary>
    public Rect? GetAnimationRowExpanderRectangle(TimelineAnimationRowId id)
    {
        var (owner, animation) = FindAnimationRow(id);
        return owner is not null && animation is not null
            ? animation.ExpanderRectangle(RowY(owner), HeaderWidth) : null;
    }

    /// <summary>取得独立属性行状态，包括被整体折叠暂时隐藏的属性行。</summary>
    public bool IsAnimationRowCollapsed(TimelineAnimationRowId id) => collapsedAnimationRows.Contains(id);

    internal bool TryRequestAnimationRowCollapse(Point point) => TryRequestAnimationRowCollapse(point, RowAt(point.Y));

    private (TimelineRow? Owner, TimelineAnimationRow? Animation) FindAnimationRow(TimelineAnimationRowId id)
    {
        var owner = rows.FirstOrDefault(row => row.Id == id.OwnerId &&
            (row.TrackId.HasValue ? TimelineRowScope.SUBTITLE_TRACK : TimelineRowScope.SCENE_LAYER) == id.Scope);
        return (owner, owner?.Animations.FirstOrDefault(animation => animation.Id == id));
    }

    private bool TryRequestAnimationRowCollapse(Point point, TimelineRow? row)
    {
        var animation = BodyRectangle().Contains(point)
            ? row?.Animations.FirstOrDefault(animation => animation.ExpanderRectangle(RowY(row), HeaderWidth).Contains(point))
            : null;
        if (animation is null)
        {
            return false;
        }

        CancelDrag();
        AnimationRowCollapseRequested?.Invoke(this, new(animation.Id, !animation.IsCollapsed));
        return true;
    }

    private bool IsAnimationRowExpanderPoint(Point point) => RowAt(point.Y) is { } row &&
        row.Animations.Any(animation => animation.ExpanderRectangle(RowY(row), HeaderWidth).Contains(point));

    private bool TryRequestAnimationRowContext(Point point)
    {
        if (FindClipAnimationContext(point) is { } context)
        {
            AnimationRowContextRequested?.Invoke(this, context);
            return true;
        }
        var row = BodyRectangle().Contains(point) ? RowAt(point.Y) : null;
        var animation = row?.Animations.FirstOrDefault(value => value.Rectangle(RowY(row), HeaderWidth, Bounds.Width).Contains(point));
        if (animation is null)
        {
            return false;
        }
        AnimationRowContextRequested?.Invoke(this, new(animation.Id));
        return true;
    }

    private TimelineAnimationRowContextEventArgs? FindClipAnimationContext(Point point)
    {
        if (FindKeyframe(point) is { } marker &&
            animationRowsByTarget.GetValueOrDefault((marker.Identity.LayerId, marker.Identity.Target)) is { } markerRow)
        {
            return new(markerRow.Id, marker.Identity.LayerId);
        }
        var row = BodyRectangle().Contains(point) ? RowAt(point.Y) : null;
        var animation = row?.Animations.FirstOrDefault(value => value.Rectangle(RowY(row), HeaderWidth, Bounds.Width).Contains(point));
        if (animation is null)
        {
            return null;
        }
        return FindAnimationCurveClip(point, row!, animation) is { } clipId ? new(animation.Id, clipId) : null;
    }

    private Guid? FindAnimationCurveClip(Point point, TimelineRow row, TimelineAnimationRow animation)
    {
        Guid? hit = null;
        var nearest = 36d;
        foreach (var source in ClipsForRow(row).Reverse())
        {
            var layer = DisplayedLayer(source);
            var startX = Math.Max(HeaderWidth, X(Seconds(layer.Start)));
            var endX = Math.Min(Bounds.Width, X(Seconds(layer.End)));
            if (endX <= startX || point.X < startX || point.X > endX)
            {
                continue;
            }
            var tracks = TracksFor(layer);
            foreach (var target in animation.TargetsFor(layer.Id))
            {
                if (CurveRectangle(layer.Id, target) is not { } curve || !curve.Inflate(6).Contains(point) ||
                    DisplayedTrack(layer, target, tracks) is not { } track)
                {
                    continue;
                }
                if (animation.IsCollapsed)
                {
                    if (Math.Abs(point.Y - curve.Center.Y) <= 6)
                    {
                        return layer.Id;
                    }
                    continue;
                }
                var range = CachedValueRange(source, track);
                var value = track.InitialValue ?? track.Keyframes[0].Value;
                var samples = Math.Max(2, (int)(endX - startX) / 3);
                for (var component = 0; component < value.ComponentCount; component++)
                {
                    var area = ComponentCurve(curve, value, component);
                    var limits = ComponentRange(value, component, range);
                    Point? previous = null;
                    for (var index = 0; index <= samples; index++)
                    {
                        var x = startX + index * (endX - startX) / samples;
                        var sample = AnimationCurvePoint(layer, track, area, limits, component, x);
                        if (previous is { } first)
                        {
                            var distance = DistanceSquaredToCurveSegment(point, first, sample);
                            if (distance <= 36 && (hit is null || distance < nearest))
                            {
                                hit = layer.Id;
                                nearest = distance;
                            }
                        }
                        previous = sample;
                    }
                }
            }
        }
        return hit;
    }

    private Point AnimationCurvePoint(ProjectLayer layer, AnimationTrack track, Rect area,
        (double Minimum, double Maximum) range, int component, double x)
    {
        var time = new MediaTime((long)Math.Round((ViewStart + (x - HeaderWidth) / PixelsPerSecond) * 1000000), 1000000) -
            layer.Start + layer.AnimationOffset;
        return new(x, ValueY(SceneEvaluator.EvaluateTrack(track, time).GetComponent(component),
            range.Minimum, range.Maximum, area));
    }

    private static double DistanceSquaredToCurveSegment(Point point, Point start, Point end)
    {
        var segment = end - start;
        var length = segment.X * segment.X + segment.Y * segment.Y;
        var relative = point - start;
        var ratio = length == 0 ? 0 : Math.Clamp((relative.X * segment.X + relative.Y * segment.Y) / length, 0, 1);
        var offset = point - (start + segment * ratio);
        return offset.X * offset.X + offset.Y * offset.Y;
    }
}
