using AegiNext.Core.Projects;

namespace AegiNext.Application.SubtitleFormats;

internal sealed record AssTextAnimationOperation(AssTransformTiming Timing, AnimationValue Value,
    int ComponentMask = 0, AnimationTransformMode Mode = AnimationTransformMode.INTERPOLATE_TO, int KaraokeCandidate = 0);
