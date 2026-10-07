using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class BlockingPreviewSource : IVideoFrameSource
{
    private readonly PreviewTestSource source = new(1, 0, 5000, 10000, 15000, 20000);
    private readonly ConcurrentDictionary<MediaTime, ConcurrentQueue<BlockedPreviewSeek>> pending = new();
    private readonly ConcurrentBag<BlockedPreviewSeek> registered = [];

    internal ConcurrentQueue<MediaTime> SeekTargets { get; } = new();
    internal IEnumerable<PreviewTestFrame> IssuedFrames => source.IssuedFrames;
    internal int DisposeCount => source.DisposeCount;
    internal int ReadCount => source.ReadCount;

    internal BlockedPreviewSeek BlockNextSeek(MediaTime target)
    {
        var request = new BlockedPreviewSeek(target);
        pending.GetOrAdd(target, _ => new()).Enqueue(request);
        registered.Add(request);
        return request;
    }

    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        return source.ReadFrame(cancellationToken);
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        SeekTargets.Enqueue(target);
        if (pending.TryGetValue(target, out var requests) && requests.TryDequeue(out var request))
        {
            request.Wait(cancellationToken);
        }

        return source.SeekFrame(target, cancellationToken);
    }

    public void Cancel()
    {
        source.Cancel();
        foreach (var request in registered)
        {
            request.Release();
        }
    }

    public void Dispose()
    {
        source.Dispose();
    }
}
