using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Tests;

internal sealed class PreviewTestFrame : IVideoFrame
{
    private int disposeCount;

    internal PreviewTestFrame(long timestamp, byte marker)
    {
        Info = new(new NativeDecodedFrameInfo
        {
            width = 1,
            height = 1,
            planeCount = 1,
            componentCount = 3,
            timeBaseNum = 1,
            timeBaseDen = 1000,
            flags = 1,
            pts = timestamp
        }, default, []);
        Marker = marker;
    }

    public VideoFrameInfo Info { get; }
    internal byte Marker { get; }
    internal int DisposeCount => Volatile.Read(ref disposeCount);

    public VideoPlaneInfo GetPlaneInfo(int index)
    {
        ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
        ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
        return new(0, 1, 1, 1);
    }

    public byte[] CopyPlane(int index)
    {
        _ = GetPlaneInfo(index);
        return [Marker];
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }
}
