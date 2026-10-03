namespace AegiNext.Media.Decoding;

internal sealed class VideoFrameLease(VideoFrameCacheEntry entry) : IVideoFrame
{
    private readonly Lock gate = new();
    private VideoFrameCacheEntry? owner = entry;

    public VideoFrameInfo Info { get; } = entry.Info;

    public VideoPlaneInfo GetPlaneInfo(int index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(owner is null, this);
            return owner.Use(frame => frame.GetPlaneInfo(index));
        }
    }

    public byte[] CopyPlane(int index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(owner is null, this);
            return owner.Use(frame => frame.CopyPlane(index));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            var previous = owner;
            owner = null;
            previous?.Release();
        }
    }

    internal TResult UseHandle<TResult>(Func<DecodedFrameHandle, TResult> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(owner is null, this);
            return owner.Use(frame => frame switch
            {
                DecodedVideoFrame decoded => decoded.UseHandle(operation),
                VideoFrameLease lease => lease.UseHandle(operation),
                _ => throw new NotSupportedException("预览转换需要拥有原生像素的解码帧。")
            });
        }
    }
}
