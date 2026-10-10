using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using Avalonia;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineAnimationRow(TimelineAnimationRowId Id,
    ImmutableDictionary<Guid, ImmutableArray<AnimationTrackTarget>> Targets, bool IsCollapsed, double Top, double Height, string? RangeLabel = null)
{
    internal AnimationProperty Property => Id.Property;
    internal ImmutableArray<AnimationTrackTarget> TargetsFor(Guid layerId) => Targets.GetValueOrDefault(layerId, []);
    internal string Title => AnimationPropertyLocalization.Get(Property) +
        (Id.TextRangeId is not null ? " · " + Localization.Get("Workbench.TextRangeAnimation") + " " + RangeLabel : string.Empty) +
        (Id.State != SubtitleAnimationState.NORMAL ? " · " + Localization.Get(Id.State == SubtitleAnimationState.ACTIVE
            ? "Workbench.ActiveAppearance" : "Workbench.InactiveAppearance") : string.Empty);
    internal Rect Rectangle(double rowY, double headerWidth, double width) =>
        new(headerWidth, rowY + Top, Math.Max(0, width - headerWidth), Height);
    internal Rect ExpanderRectangle(double rowY, double headerWidth) => new(headerWidth + 2, rowY + Top + 1, 20, 18);
}
