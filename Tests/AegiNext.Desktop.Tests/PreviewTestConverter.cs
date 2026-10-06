using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests;

internal sealed class PreviewTestConverter : IVideoPreviewConverter
{
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int blockNext;
    private int disposeCount;
    private int activeCount;
    private int maximumActiveCount;
    private int conversionCount;

    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int DisposeCount => Volatile.Read(ref disposeCount);
    internal int MaximumActiveCount => Volatile.Read(ref maximumActiveCount);
    internal int ConversionCount => Volatile.Read(ref conversionCount);
    internal Action<byte>? ConversionWork { get; init; }

    internal void BlockNextConversion()
    {
        Volatile.Write(ref blockNext, 1);
    }

    internal void Release()
    {
        release.TrySetResult();
    }

    /// <summary>
    /// 模拟进入后无法中断的原生转换，让控制器负责拒绝取消后迟到的结果。
    /// </summary>
    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var active = Interlocked.Increment(ref activeCount);
        Interlocked.Exchange(ref maximumActiveCount, Math.Max(active, MaximumActiveCount));
        try
        {
            var marker = frame.CopyPlane(0)[0];
            if (Interlocked.Exchange(ref blockNext, 0) != 0)
            {
                Entered.TrySetResult();
                release.Task.WaitAsync(TimeSpan.FromSeconds(10), CancellationToken.None).GetAwaiter().GetResult();
            }

            Interlocked.Increment(ref conversionCount);
            ConversionWork?.Invoke(marker);
            return new(1, 1, [marker, 0, 0, 255]);
        }
        finally
        {
            Interlocked.Decrement(ref activeCount);
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }
}
