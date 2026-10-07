using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controllers;

internal sealed record AudioOutputPlaybackScope(MediaTime VideoPosition, MediaTime AudioPosition, bool WasPlaying,
    MediaTimeRange? Range, bool Loop, bool AudioOnly, CancellationToken OwnerToken);
