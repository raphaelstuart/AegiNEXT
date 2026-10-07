using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests;

internal sealed class LatencyPreviewSource(int frameCount, int frameRate, TimeSpan readLatency) : IVideoFrameSource
{
    private int position;
    private int readCount;
    private int seekCount;
    private int cancelCount;
    private int disposeCount;

    internal ConcurrentQueue<PreviewTestFrame> IssuedFrames { get; } = new();
    internal int ReadCount => Volatile.Read(ref readCount);
    internal int SeekCount => Volatile.Read(ref seekCount);
    internal int DisposeCount => Volatile.Read(ref disposeCount);

    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        Interlocked.Increment(ref readCount);
        if (cancellationToken.WaitHandle.WaitOne(readLatency))
        {
            cancellationToken.ThrowIfCancellationRequested();
        }
        CheckState(cancellationToken);
        return position < frameCount ? Create(position++) : null;
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        Interlocked.Increment(ref seekCount);
        var index = 0;
        while (index + 1 < frameCount && new MediaTime(index + 1, frameRate) <= target)
        {
            index++;
        }
        position = index + 1;
        return Create(index);
    }

    public void Cancel()
    {
        Interlocked.Increment(ref cancelCount);
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }

    private PositionedVideoFrame Create(int index)
    {
        var frame = new PreviewTestFrame(index, (byte)(index % 200), frameRate);
        IssuedFrames.Enqueue(frame);
        return new(frame, new(index, frameRate),
            index + 1 < frameCount ? new MediaTime(index + 1, frameRate) : null,
            reachedEnd: index + 1 == frameCount);
    }

    private void CheckState(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref cancelCount) != 0)
        {
            throw new OperationCanceledException("The latency source was permanently cancelled.");
        }
    }
}
