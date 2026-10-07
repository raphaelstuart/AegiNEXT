using System.Diagnostics;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests.Controllers;

internal sealed class MeasuredNativePreviewConverter(NativePreviewTimingMetrics metrics) : IVideoPreviewConverter
{
    private readonly SdrVideoConverter converter = new(new(1920, 1080));
    private int disposeCount;

    internal int DisposeCount => Volatile.Read(ref disposeCount);

    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            return converter.Convert(frame, cancellationToken);
        }
        finally
        {
            metrics.ConversionMilliseconds.Enqueue(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
        converter.Dispose();
    }
}
