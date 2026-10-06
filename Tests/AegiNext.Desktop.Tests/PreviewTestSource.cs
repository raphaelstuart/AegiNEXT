using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests;

internal sealed class PreviewTestSource(byte markerBase, params long[] timestamps) : IVideoFrameSource
{
    private int position;
    private int cancelCount;
    private int disposeCount;

    internal ConcurrentQueue<PreviewTestFrame> IssuedFrames { get; } = new();
    internal int CancelCount => Volatile.Read(ref cancelCount);
    internal int DisposeCount => Volatile.Read(ref disposeCount);

    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        return position < timestamps.Length ? Create(position++, false) : null;
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        if (timestamps.Length == 0)
        {
            return null;
        }

        var index = 0;
        while (index + 1 < timestamps.Length && new MediaTime(timestamps[index + 1], 1000) <= target)
        {
            index++;
        }

        position = index + 1;
        return Create(index, target < new MediaTime(timestamps[0], 1000));
    }

    public void Cancel()
    {
        Interlocked.Increment(ref cancelCount);
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }

    private PositionedVideoFrame Create(int index, bool beforeFirst)
    {
        var frame = new PreviewTestFrame(timestamps[index], checked((byte)(markerBase + index)));
        IssuedFrames.Enqueue(frame);
        return new(frame, new(timestamps[index], 1000),
            index + 1 < timestamps.Length ? new MediaTime(timestamps[index + 1], 1000) : null,
            beforeFirst, index + 1 == timestamps.Length);
    }

    private void CheckState(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (CancelCount != 0)
        {
            throw new OperationCanceledException("The test source was permanently cancelled.");
        }
    }
}
