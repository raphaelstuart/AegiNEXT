using AegiNext.Media.Decoding;

namespace AegiNext.Media.Playback;

/// <summary>
/// 持有一次显示交付的帧；从会话读取后由消费者负责释放。
/// </summary>
public sealed class VideoPresentation : IDisposable
{
    private int disposed;

    /// <summary>
    /// 接管定位帧的唯一所有权，并记录其会话代数。
    /// </summary>
    public VideoPresentation(PositionedVideoFrame positionedFrame, long generation)
    {
        ArgumentNullException.ThrowIfNull(positionedFrame);
        ArgumentOutOfRangeException.ThrowIfNegative(generation);
        PositionedFrame = positionedFrame;
        Generation = generation;
    }

    public PositionedVideoFrame PositionedFrame { get; }

    public long Generation { get; }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            PositionedFrame.Dispose();
        }
    }
}
