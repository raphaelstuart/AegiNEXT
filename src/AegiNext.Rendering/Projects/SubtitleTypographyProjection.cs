using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

internal sealed record SubtitleTypographyProjection(SubtitleLine Source,
    ImmutableDictionary<AnimationTrackTarget, AnimationValue> Values, SubtitleLine Subtitle);
