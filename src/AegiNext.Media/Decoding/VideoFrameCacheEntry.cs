using AegiNext.Core.Timing;

namespace AegiNext.Media.Decoding;

internal sealed class VideoFrameCacheEntry(IVideoFrame frame, MediaTime time, MediaTime? nextTime, bool reachedEnd, long bytes)
{
    private readonly Lock gate = new();
    private int references = 1;

    internal VideoFrameInfo Info { get; } = frame.Info;
    internal MediaTime Time { get; } = time;
    internal MediaTime? NextTime { get; } = nextTime;
    internal bool ReachedEnd { get; } = reachedEnd;
    internal long Bytes { get; } = bytes;

    internal PositionedVideoFrame Acquire()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(references == 0, this);
            references++;
            return new(new VideoFrameLease(this), Time, NextTime, reachedEnd: ReachedEnd);
        }
    }

    internal TResult Use<TResult>(Func<IVideoFrame, TResult> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(references == 0, this);
            return operation(frame);
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            if (--references == 0)
            {
                frame.Dispose();
            }
        }
    }
}
