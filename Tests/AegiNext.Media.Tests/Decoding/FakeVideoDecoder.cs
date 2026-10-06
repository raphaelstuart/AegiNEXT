using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

internal sealed class FakeVideoDecoder(params long?[] timestamps) : IVideoDecoder
{
    private int position;
    private bool cancelled;

    public MediaTimeBase StreamTimeBase { get; } = new(1, 1000);

    internal List<FakeVideoFrame> IssuedFrames { get; } = [];

    internal List<MediaTime> SeekTargets { get; } = [];

    internal Func<int, FakeVideoFrame>? FrameFactory { get; set; }

    internal int SeekLandingIndex { get; set; }

    internal int ReadCount { get; private set; }

    internal int CancelCount { get; private set; }

    internal int DisposeCount { get; private set; }

    public IVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        ReadCount++;
        if (position == timestamps.Length)
        {
            return null;
        }

        var frame = FrameFactory?.Invoke(position) ?? new(timestamps[position], position);
        position++;
        IssuedFrames.Add(frame);
        return frame;
    }

    public void SeekToKeyFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        SeekTargets.Add(target);
        position = SeekLandingIndex;
    }

    public void Cancel()
    {
        CancelCount++;
        cancelled = true;
    }

    public void Dispose()
    {
        DisposeCount++;
    }

    private void CheckState(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(DisposeCount > 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (cancelled)
        {
            throw new OperationCanceledException("Fake decoder cancellation is terminal.");
        }
    }
}
