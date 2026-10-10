using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.SubtitleFormats;

internal sealed record AssTextAnimationSnapshot(AnimationValue Initial, ImmutableArray<AssTextAnimationOperation> Operations)
{
    internal bool Equivalent(AssTextAnimationSnapshot other) => Initial == other.Initial && Operations.SequenceEqual(other.Operations);
}
