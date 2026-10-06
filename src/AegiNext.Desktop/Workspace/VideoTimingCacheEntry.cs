using AegiNext.Core.Media;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

internal sealed record VideoTimingCacheEntry(string Path, int StreamIndex, MediaTime Origin, long Epoch,
    long? FileLength, DateTime? LastWriteTime, DateTime? CreationTime, VideoTimingIndex Index);
