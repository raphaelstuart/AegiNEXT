using AegiNext.Core.Timing;
using AegiNext.Desktop.Settings;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace AegiNext.Desktop.Controls;

public sealed partial class SubtitleTimelineControl
{
    private const double CLIP_BOUNDARY_WIDTH = 1.5;
    private TimelineClipPalette clipPalette = new();
    private SolidColorBrush selectedClipBrush = new();
    private SolidColorBrush inactiveClipBrush = new();
    private SolidColorBrush selectedRangeBrush = new();
    private SolidColorBrush inactiveRangeBrush = new();
    private SolidColorBrush invalidRangeBrush = new();
    private Pen selectedStartPen = new(Brushes.Transparent, CLIP_BOUNDARY_WIDTH);
    private Pen selectedEndPen = new(Brushes.Transparent, CLIP_BOUNDARY_WIDTH);
    private Pen inactiveBoundaryPen = new(Brushes.Transparent, 1);
    private Pen selectedClipBorder = new(Brushes.Transparent, 1.5);
    private Pen inactiveClipBorder = new(Brushes.Transparent, 1);
    private IReadOnlyList<TimelineRow>? projectedRows;
    private Guid[] projectedSelection = [];
    private (TimelineDragMode Mode, Guid Id, MediaTime Start, MediaTime End, MediaTime Origin, bool Valid) projectedDrag;
    private IReadOnlyList<TimelineClipRange> projectedFills = [];
    private IReadOnlyList<(MediaTime Time, bool IsStart, bool IsSelected, bool IsInvalid)> projectedBoundaries = [];

    /// <summary>应用个人 clip 配色，仅刷新绘制，不改变工程、音频分析或视口。</summary>
    public void SetClipPalette(TimelineClipPalette value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        if (clipPalette == value)
        {
            return;
        }

        clipPalette = value;
        RefreshClipAppearance();
        InvalidateVisual();
    }

    private void RefreshClipAppearance()
    {
        var palette = TimelineClipPalettes.Resolve(clipPalette, ActualThemeVariant != ThemeVariant.Dark);
        selectedClipBrush = new(AudioGraphColorRamp.Parse(palette.SelectedClip));
        inactiveClipBrush = new(AudioGraphColorRamp.Parse(palette.InactiveClip));
        selectedRangeBrush = new(AudioGraphColorRamp.Parse(palette.SelectedRangeFill));
        inactiveRangeBrush = new(AudioGraphColorRamp.Parse(palette.InactiveRangeFill));
        selectedStartPen = new(new SolidColorBrush(AudioGraphColorRamp.Parse(palette.StartLine)), CLIP_BOUNDARY_WIDTH);
        selectedEndPen = new(new SolidColorBrush(AudioGraphColorRamp.Parse(palette.EndLine)), CLIP_BOUNDARY_WIDTH);
        inactiveBoundaryPen = new(inactiveClipBrush, 1);
        inactiveClipBorder = new(inactiveClipBrush, 1);
        selectedClipBorder = new(selectedClipBrush, 1.5);
        var invalid = drawingPalette.InvalidClip.Color;
        invalidRangeBrush = new(Color.FromArgb((byte)Math.Round(invalid.A * 0.15), invalid.R, invalid.G, invalid.B));
    }

    private void RefreshClipRangeProjection()
    {
        var state = (dragMode, dragId, pendingStart, pendingEnd, originalStart, validDrop);
        if (ReferenceEquals(projectedRows, rows) && projectedDrag == state && selectedIds.SetEquals(projectedSelection))
        {
            return;
        }

        var ranges = rows.SelectMany(ClipsForRow).DistinctBy(clip => clip.Id).Select(clip =>
        {
            var layer = DisplayedLayer(clip);
            return new TimelineClipRange(layer.Start, layer.End, selectedIds.Contains(clip.Id), IsInvalidClipDrag(clip.Id));
        }).ToArray();
        projectedFills = TimelineClipRangeProjection.Partition(ranges);
        projectedBoundaries = ranges.SelectMany(range => new[]
            {
                (Time: range.Start, IsStart: true, range.IsSelected, range.IsInvalid),
                (Time: range.End, IsStart: false, range.IsSelected, range.IsInvalid)
            }).GroupBy(boundary => boundary.Time).Select(group => group.OrderByDescending(boundary => boundary.IsInvalid)
                .ThenByDescending(boundary => boundary.IsSelected).ThenBy(boundary => boundary.IsStart).First())
            .OrderBy(boundary => boundary.Time).ToArray();
        projectedRows = rows;
        projectedDrag = state;
        projectedSelection = selectedIds.ToArray();
    }

    private bool IsInvalidClipDrag(Guid id) => HasActiveDrag && !validDrop && (dragId == id || movingClips.ContainsKey(id));

    private void DrawClipRangeFills(DrawingContext context, Rect body)
    {
        foreach (var range in projectedFills)
        {
            var left = Math.Max(body.Left, X(Seconds(range.Start)));
            var right = Math.Min(body.Right, X(Seconds(range.End)));
            if (left >= body.Right)
            {
                break;
            }
            if (left >= right)
            {
                continue;
            }

            var brush = range.IsInvalid ? invalidRangeBrush : range.IsSelected ? selectedRangeBrush : inactiveRangeBrush;
            context.DrawRectangle(brush, null, new(left, body.Top, right - left, body.Height));
        }
    }

    private void DrawClipBoundaries(DrawingContext context)
    {
        var body = BodyRectangle();
        using var clip = context.PushClip(body);
        foreach (var boundary in projectedBoundaries)
        {
            var x = X(Seconds(boundary.Time));
            if (x > body.Right)
            {
                break;
            }
            if (x < body.Left)
            {
                continue;
            }
            var pen = boundary.IsInvalid ? new Pen(drawingPalette.InvalidClip, CLIP_BOUNDARY_WIDTH)
                : !boundary.IsSelected ? inactiveBoundaryPen : boundary.IsStart ? selectedStartPen : selectedEndPen;
            DrawClipBoundary(context, body, x, pen);
        }
    }

    private static void DrawClipBoundary(DrawingContext context, Rect body, double x, Pen pen)
    {
        if (x >= body.Left && x <= body.Right)
        {
            context.DrawLine(pen, new(x, body.Top), new(x, body.Bottom));
        }
    }
}
