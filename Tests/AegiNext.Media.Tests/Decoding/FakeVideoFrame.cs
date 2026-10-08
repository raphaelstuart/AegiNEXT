using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

internal sealed class FakeVideoFrame : IVideoFrame
{
    private int disposeCount;

    internal FakeVideoFrame(long? pts, int marker, MediaTimeBase? timeBase = null, long? bestEffortTimestamp = null,
        NativeFrameDisplayTiming? displayTiming = null)
    {
        var sourceTimeBase = timeBase ?? new(1, 1000);
        var value = new NativeDecodedFrameInfo
        {
            width = 1,
            height = 1,
            planeCount = 1,
            componentCount = 1,
            timeBaseNum = checked((int)sourceTimeBase.Numerator),
            timeBaseDen = checked((int)sourceTimeBase.Denominator),
            streamTimeBaseNum = checked((int)sourceTimeBase.Numerator),
            streamTimeBaseDen = checked((int)sourceTimeBase.Denominator),
            flags = (pts.HasValue ? 1u : 0u) | (bestEffortTimestamp.HasValue ? 2u : 0u),
            pts = pts ?? 0,
            bestEffortTimestamp = bestEffortTimestamp ?? 0
        };
        Info = new(value, default, [], displayTiming);
        Marker = marker;
    }

    public VideoFrameInfo Info { get; }

    internal int Marker { get; }
    internal int SourceStride { get; init; } = 1;
    internal Exception? PlaneInfoFailure { get; init; }

    internal int DisposeCount => Volatile.Read(ref disposeCount);

    public VideoPlaneInfo GetPlaneInfo(int index)
    {
        ObjectDisposedException.ThrowIf(DisposeCount > 0, this);
        ArgumentOutOfRangeException.ThrowIfNotEqual(index, 0);
        if (PlaneInfoFailure is { } failure)
        {
            throw failure;
        }

        return new(0, 1, 1, SourceStride);
    }

    public byte[] CopyPlane(int index)
    {
        _ = GetPlaneInfo(index);
        return [checked((byte)Marker)];
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }
}
