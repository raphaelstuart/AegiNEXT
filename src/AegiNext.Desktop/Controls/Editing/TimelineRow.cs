using AegiNext.Core.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineRow(Guid Id, Guid? TrackId, string Name, IReadOnlyList<ProjectLayer> Clips,
    int Depth, bool IsGroup, bool IsCollapsed, double Top, double Height, IReadOnlyList<TimelineAnimationRow> Animations,
    string? StylePresetName = null, bool AutoApplyStyle = true)
{
    private const double STYLE_BADGE_GAP = 2;
    internal double CurveHeight => Animations.Sum(row => row.Height);
    internal Rect? StyleBadgeRectangle(double rowY, double headerWidth) => StylePresetName is null
        ? null : new Rect(26 + Depth * 8, ExpanderRectangle(rowY).Bottom + STYLE_BADGE_GAP,
            Math.Max(0, headerWidth - 34 - Depth * 8), 18);
    internal Rect ExpanderRectangle(double rowY) => new(4 + Depth * 8, rowY + 4, 20, 20);
}
