using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Audio;

public sealed class AudioPlaybackRangeTests
{
    [Fact]
    public async Task ExclusiveRangeEndTruncatesPcmAndStopsAfterTheDeviceDrains()
    {
        var source = new FakeAudioSource(0);
        var output = new FakeAudioOutput();
        await using var session = new AudioPlaybackSession(source, output, MediaTime.Zero);
        var range = new MediaTimeRange(new(2, 48000), new(13, 96000));
        await session.SetPlaybackRangeAsync(range);

        Assert.Equal(5, output.QueuedFrames);
        Assert.Equal(range.Start, source.LastSeek);
        Assert.Equal(range.Start, session.Position);
        await session.PlayAsync();
        var written = output.Written.Length;
        output.Consume(1000);
        await EventuallyAsync(() => session.ReachedPlaybackRangeEnd);

        Assert.True(output.Paused);
        Assert.Equal(range.End, session.Position);
        Assert.Equal(0, output.QueuedFrames);
        Assert.Equal(written, output.Written.Length);
    }

    [Fact]
    public async Task SubsampleRangeQueuesNoSampleAndKeepsExactPositionInsideTheRange()
    {
        var output = new FakeAudioOutput();
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, MediaTime.Zero);
        var range = new MediaTimeRange(new(1, 100000), new(2, 100000));
        await session.SetPlaybackRangeAsync(range);

        Assert.Equal(0, output.QueuedFrames);
        Assert.Equal(range.Start, session.Position);
        await session.PlayAsync();
        await EventuallyAsync(() => session.ReachedPlaybackRangeEnd);
        Assert.Equal(range.End, session.Position);
    }

    [Fact]
    public async Task PausedDeviceDrainDoesNotCountPausedTimeWhenPlaybackResumes()
    {
        var output = new FakeAudioOutput { LatencyFrames = 24000 };
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, MediaTime.Zero);
        var range = new MediaTimeRange(MediaTime.Zero, new(1, 1000));
        await session.SetPlaybackRangeAsync(range);
        await session.PlayAsync();
        output.Consume(48000);
        await Task.Delay(60);
        await session.PauseAsync();
        Assert.False(session.ReachedPlaybackRangeEnd);

        await Task.Delay(600);
        await session.PlayAsync();
        await Task.Delay(50);

        Assert.False(session.ReachedPlaybackRangeEnd);
        Assert.False(output.Paused);
        await EventuallyAsync(() => session.ReachedPlaybackRangeEnd);
        Assert.Equal(range.End, session.Position);
    }

    [Fact]
    public async Task NewRangeResetsEndStateAndClearingRestoresUnboundedPrebuffering()
    {
        var source = new FakeAudioSource(0);
        var output = new FakeAudioOutput();
        await using var session = new AudioPlaybackSession(source, output, MediaTime.Zero);
        var first = new MediaTimeRange(new(3), new(3001, 1000));
        await session.SetPlaybackRangeAsync(first);
        await session.PlayAsync();
        output.Consume(48000);
        await EventuallyAsync(() => session.ReachedPlaybackRangeEnd);

        var second = new MediaTimeRange(new(4), new(4002, 1000));
        await session.SetPlaybackRangeAsync(second);
        Assert.False(session.ReachedPlaybackRangeEnd);
        Assert.True(output.Paused);
        Assert.Equal(96, output.QueuedFrames);
        Assert.Equal(second.Start, session.Position);

        await session.SetPlaybackRangeAsync(null);
        Assert.True(output.QueuedFrames > 96);
        Assert.Equal(second.Start, source.LastSeek);
        Assert.False(session.ReachedPlaybackRangeEnd);
        Assert.True(output.Paused);
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
