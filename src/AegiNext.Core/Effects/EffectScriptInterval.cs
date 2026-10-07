using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Effects;

internal sealed record EffectScriptInterval(AnimationTrackTarget Target, MediaTime Start, MediaTime End,
    int Line, int Column);
