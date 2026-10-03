using AegiNext.Core.Timing;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Controllers;

internal sealed record VideoPreviewDelivery(VideoPlaybackSession Session, long Generation, long Revision,
    MediaTime Time, MediaTime? NextTime);
