using AegiNext.Core.Projects;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineRow(Guid Id, Guid? TrackId, string Name, IReadOnlyList<ProjectLayer> Clips,
    int Depth, bool IsGroup, bool IsCollapsed, double Top, double Height, IReadOnlyList<TimelineAnimationRow> Animations,
    string? StylePresetName = null, bool AutoApplyStyle = true)
{
    internal double CurveHeight => Animations.Sum(row => row.Height);
    internal Rect? StyleBadgeRectangle(double rowY, double headerWidth) => StylePresetName is null
        ? null : new Rect(46 + Depth * 8, rowY + 23, Math.Max(0, headerWidth - 54 - Depth * 8), 18);
}
