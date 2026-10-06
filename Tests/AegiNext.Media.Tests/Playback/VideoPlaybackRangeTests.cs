using AegiNext.Core.Timing;
using AegiNext.Media.Playback;

namespace AegiNext.Media.Tests.Playback;

public sealed class VideoPlaybackRangeTests
{
    [Fact]
    public async Task SubframeRangeHoldsTheCoveringFrameAndEndsAtItsExactBoundary()
    {
        var clock = new ManualPlaybackTimeProvider();
        var source = new FakeVideoFrameSource(0, 40, 100);
        await using var session = new VideoPlaybackSession(_ => source, clock);
        await session.OpenAsync();
        using var initial = await session.ReadPresentationAsync();
        var range = new MediaTimeRange(new(10, 1000), new(20, 1000));
        session.SetPlaybackRange(range);
        await session.SeekAsync(range.Start);
        using var covering = await session.ReadPresentationAsync();
        Assert.NotNull(covering);
        Assert.Equal(MediaTime.Zero, covering.PositionedFrame.Time);
        await session.PlayAsync();
        await EventuallyAsync(() => clock.ActiveTimerCount != 0);
        clock.Advance(TimeSpan.FromMilliseconds(10));
        await EventuallyAsync(() => session.Snapshot.State == VideoPlaybackState.ENDED);

        Assert.Equal(range.End, session.Snapshot.Position);
        Assert.Equal(MediaTime.Zero, session.Snapshot.DisplayTime);
        Assert.DoesNotContain(source.IssuedFrames, frame => frame.Marker == 1);
    }

    [Fact]
    public async Task FinalFrameRemainsVisibleUntilRangeEndAndClearingAllowsLaterFrames()
    {
        var clock = new ManualPlaybackTimeProvider();
        await using var session = new VideoPlaybackSession(_ => new FakeVideoFrameSource(0, 40, 100), clock);
        await session.OpenAsync();
        using var initial = await session.ReadPresentationAsync();
        var range = new MediaTimeRange(new(110, 1000), new(160, 1000));
        session.SetPlaybackRange(range);
        await session.SeekAsync(range.Start);
        using var final = await session.ReadPresentationAsync();
        await session.PlayAsync();
        await EventuallyAsync(() => clock.ActiveTimerCount != 0);
        Assert.Equal(VideoPlaybackState.PLAYING, session.Snapshot.State);
        clock.Advance(TimeSpan.FromMilliseconds(50));
        await EventuallyAsync(() => session.Snapshot.State == VideoPlaybackState.ENDED);
        Assert.Equal(range.End, session.Snapshot.Position);

        session.SetPlaybackRange(null);
        await session.SeekAsync(new(50, 1000));
        using var unbounded = await session.ReadPresentationAsync();
        Assert.NotNull(unbounded);
        Assert.Equal(new MediaTime(40, 1000), unbounded.PositionedFrame.Time);
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            await Task.Delay(5, timeout.Token);
        }
    }
}
