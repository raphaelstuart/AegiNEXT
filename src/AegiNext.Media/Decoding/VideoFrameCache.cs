using AegiNext.Core.Timing;

namespace AegiNext.Media.Decoding;

internal sealed class VideoFrameCache(int maximumFrames, long maximumBytes) : IDisposable
{
    private readonly Dictionary<MediaTime, LinkedListNode<VideoFrameCacheEntry>> entries = new();
    private readonly LinkedList<VideoFrameCacheEntry> recency = new();
    private long bytes;

    internal PositionedVideoFrame Store(PositionedVideoFrame frame)
    {
        if (maximumFrames == 0 || maximumBytes == 0)
        {
            return frame;
        }

        try
        {
            var estimate = EstimateBytes(frame.Frame);
            if (estimate > maximumBytes)
            {
                return frame;
            }

            var entry = new VideoFrameCacheEntry(frame.DetachFrame(), frame.Time, frame.NextFrameTime, frame.ReachedEnd, estimate);
            frame.Dispose();
            PositionedVideoFrame? lease = null;
            var retained = false;
            try
            {
                lease = entry.Acquire();
                if (entries.Remove(entry.Time, out var old))
                {
                    recency.Remove(old);
                    bytes -= old.Value.Bytes;
                    old.Value.Release();
                }

                while (entries.Count >= maximumFrames || bytes > maximumBytes - estimate)
                {
                    EvictFirst();
                }

                var node = recency.AddLast(entry);
                try
                {
                    entries.Add(entry.Time, node);
                }
                catch
                {
                    recency.Remove(node);
                    throw;
                }

                retained = true;
                bytes += estimate;

                return lease;
            }
            catch
            {
                lease?.Dispose();
                if (!retained)
                {
                    entry.Release();
                }

                throw;
            }
        }
        catch
        {
            frame.Dispose();
            throw;
        }
    }

    internal PositionedVideoFrame? FindExact(MediaTime time)
    {
        return entries.TryGetValue(time, out var node) ? Acquire(node) : null;
    }

    internal PositionedVideoFrame? FindContaining(MediaTime target)
    {
        if (entries.TryGetValue(target, out var exact))
        {
            return Acquire(exact);
        }

        for (var node = recency.Last; node is not null; node = node.Previous)
        {
            var frame = node.Value;
            if (frame.Time <= target && (frame.NextTime is null || target < frame.NextTime.Value))
            {
                return Acquire(node);
            }
        }

        return null;
    }

    public void Dispose()
    {
        while (recency.Count != 0)
        {
            EvictFirst();
        }
    }

    private PositionedVideoFrame Acquire(LinkedListNode<VideoFrameCacheEntry> node)
    {
        recency.Remove(node);
        recency.AddLast(node);
        return node.Value.Acquire();
    }

    private void EvictFirst()
    {
        var first = recency.First!;
        recency.RemoveFirst();
        entries.Remove(first.Value.Time);
        bytes -= first.Value.Bytes;
        first.Value.Release();
    }

    private static long EstimateBytes(IVideoFrame frame)
    {
        long total = 0;
        for (var index = 0; index < frame.Info.PlaneCount; index++)
        {
            var plane = frame.GetPlaneInfo(index);
            total = checked(total + Math.Max(Math.Abs((long)plane.SourceStride), plane.RowBytes) * plane.Height);
        }

        return total;
    }
}
