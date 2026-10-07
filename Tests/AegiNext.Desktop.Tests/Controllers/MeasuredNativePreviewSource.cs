using System.Diagnostics;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class MeasuredNativePreviewSource(VideoFrameNavigator navigator, NativePreviewTimingMetrics metrics) : IVideoFrameSource
{
    private VideoDecodeSessionInfo? latestSessionInfo;
    private int disposeCount;

    internal VideoDecodeSessionInfo? SessionInfo => navigator.SessionInfo ?? Volatile.Read(ref latestSessionInfo);
    internal int DisposeCount => Volatile.Read(ref disposeCount);

    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        var before = navigator.SessionInfo;
        var started = Stopwatch.GetTimestamp();
        try
        {
            return navigator.ReadFrame(cancellationToken);
        }
        finally
        {
            var after = navigator.SessionInfo;
            if (after is not null)
            {
                Volatile.Write<VideoDecodeSessionInfo?>(ref latestSessionInfo, after);
            }
            metrics.ReadMilliseconds.Enqueue(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            if (before is not null && after is not null)
            {
                if (after.DecodeNanoseconds >= before.DecodeNanoseconds)
                {
                    metrics.NativeDecodeMillisecondsPerRead.Enqueue((after.DecodeNanoseconds - before.DecodeNanoseconds) / 1_000_000d);
                }
                if (after.DownloadNanoseconds >= before.DownloadNanoseconds)
                {
                    metrics.NativeDownloadMillisecondsPerRead.Enqueue((after.DownloadNanoseconds - before.DownloadNanoseconds) / 1_000_000d);
                }
            }
        }
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        return SeekFrame(target, static () => false, cancellationToken);
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, Func<bool> isSuperseded, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return navigator.SeekFrame(target, isSuperseded, cancellationToken);
        }
        finally
        {
            if (navigator.SessionInfo is { } after)
            {
                Volatile.Write<VideoDecodeSessionInfo?>(ref latestSessionInfo, after);
            }
            metrics.SeekMilliseconds.Enqueue(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public void Cancel()
    {
        navigator.Cancel();
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
        navigator.Dispose();
    }
}
