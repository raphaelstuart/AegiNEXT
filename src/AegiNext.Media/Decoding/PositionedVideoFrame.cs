using AegiNext.Core.Timing;

namespace AegiNext.Media.Decoding;

/// <summary>
/// 持有显示帧及真实下一帧边界；末帧不制造结束时间。
/// </summary>
public sealed class PositionedVideoFrame : IDisposable
{
    private IVideoFrame? frame;

    /// <summary>
    /// 接管帧的唯一释放责任，并记录原始媒体时间和边界状态。
    /// </summary>
    public PositionedVideoFrame(IVideoFrame frame, MediaTime time, MediaTime? nextFrameTime,
        bool isBeforeFirst = false, bool reachedEnd = false)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (nextFrameTime is { } next && next <= time)
        {
            throw new ArgumentOutOfRangeException(nameof(nextFrameTime));
        }

        if (reachedEnd && nextFrameTime is not null)
        {
            throw new ArgumentException("EOF 帧不能同时具有下一帧边界。", nameof(reachedEnd));
        }

        this.frame = frame;
        Time = time;
        NextFrameTime = nextFrameTime;
        IsBeforeFirst = isBeforeFirst;
        ReachedEnd = reachedEnd;
    }

    public IVideoFrame Frame => Volatile.Read(ref frame) ?? throw new ObjectDisposedException(nameof(PositionedVideoFrame));

    public MediaTime Time { get; }

    public MediaTime? NextFrameTime { get; }

    public bool IsBeforeFirst { get; }

    public bool ReachedEnd { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Exchange(ref frame, null)?.Dispose();
    }

    internal IVideoFrame DetachFrame()
    {
        return Interlocked.Exchange(ref frame, null) ?? throw new ObjectDisposedException(nameof(PositionedVideoFrame));
    }
}
