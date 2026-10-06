using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Controls;

internal sealed record TimelineAnimationRow(AnimationProperty Property,
    ImmutableDictionary<Guid, ImmutableArray<AnimationTrackTarget>> Targets, double Top, double Height)
{
    internal ImmutableArray<AnimationTrackTarget> TargetsFor(Guid layerId) => Targets.GetValueOrDefault(layerId, []);
    internal string Title => AnimationPropertyLocalization.Get(Property);
}
