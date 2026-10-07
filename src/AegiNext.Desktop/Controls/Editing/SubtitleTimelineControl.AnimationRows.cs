using AegiNext.Core.Projects;
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
}
