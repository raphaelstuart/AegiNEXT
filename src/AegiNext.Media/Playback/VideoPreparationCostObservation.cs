using AegiNext.Core.Timing;

namespace AegiNext.Media.Playback;

internal readonly record struct VideoPreparationCostObservation(MediaTime Cost, long Timestamp);
