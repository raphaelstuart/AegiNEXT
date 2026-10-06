using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineAnimationRow(TimelineAnimationRowId Id,
    ImmutableDictionary<Guid, ImmutableArray<AnimationTrackTarget>> Targets, bool IsCollapsed, double Top, double Height)
{
    internal AnimationProperty Property => Id.Property;
    internal ImmutableArray<AnimationTrackTarget> TargetsFor(Guid layerId) => Targets.GetValueOrDefault(layerId, []);
    internal string Title => AnimationPropertyLocalization.Get(Property);
    internal Rect Rectangle(double rowY, double headerWidth, double width) =>
        new(headerWidth, rowY + Top, Math.Max(0, width - headerWidth), Height);
    internal Rect ExpanderRectangle(double rowY, double headerWidth) => new(headerWidth + 2, rowY + Top + 1, 20, 18);
}
