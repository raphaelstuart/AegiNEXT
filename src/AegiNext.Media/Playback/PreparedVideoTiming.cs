using AegiNext.Core.Timing;

namespace AegiNext.Media.Playback;

internal sealed record PreparedVideoTiming(MediaTime Time, MediaTime? NextTime, bool ReachedEnd);
