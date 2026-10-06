using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class BlockingPreviewSeekSource(PreviewTestSource inner) : IVideoFrameSource
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int blockNext;

    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal ConcurrentQueue<MediaTime> SeekTargets { get; } = new();

    internal void BlockNextSeek()
    {
        Volatile.Write(ref blockNext, 1);
    }

    internal void Release()
    {
        release.TrySetResult();
    }

    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        return inner.ReadFrame(cancellationToken);
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        SeekTargets.Enqueue(target);
        if (Interlocked.Exchange(ref blockNext, 0) != 0)
        {
            Entered.TrySetResult();
            release.Task.WaitAsync(cancellationToken).GetAwaiter().GetResult();
        }

        return inner.SeekFrame(target, cancellationToken);
    }

    public void Cancel()
    {
        inner.Cancel();
    }

    public void Dispose()
    {
        inner.Dispose();
    }
}
