using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Playback;

internal sealed class ClockAdvancingPreparationSource(ManualPlaybackTimeProvider clock,
    TimeSpan readCost, params long[] timestamps) : IVideoFrameSource
{
    internal FakeVideoFrameSource Inner { get; } = new(timestamps);
    internal int ReadCount { get; private set; }
    internal List<MediaTime> SeekTargets { get; } = new();

    public PositionedVideoFrame? ReadFrame(CancellationToken cancellationToken = default)
    {
        ReadCount++;
        if (ReadCount > 1)
        {
            clock.Advance(readCost);
        }
        return Inner.ReadFrame(cancellationToken);
    }

    public PositionedVideoFrame? SeekFrame(MediaTime target, CancellationToken cancellationToken = default)
    {
        SeekTargets.Add(target);
        return Inner.SeekFrame(target, cancellationToken);
    }

    public void Cancel()
    {
        Inner.Cancel();
    }

    public void Dispose()
    {
        Inner.Dispose();
    }
}
