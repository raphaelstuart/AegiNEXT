using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Audio;

public sealed class AudioSystemClockTests
{
    [Fact]
    public async Task UnconsumedAudioRetainsTheExactRationalSeekTarget()
    {
        var target = new MediaTime(400001, 120000);
        var output = new ClockAudioOutput();
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, target);
        await session.PlayAsync();
        Assert.Equal(target, session.Position);
        await session.PauseAsync();
        Assert.Equal(target, session.Position);
    }

    [Fact]
    public async Task SystemClockDoesNotSubtractQueueOrDeviceLatencyAgain()
    {
        var output = new ClockAudioOutput();
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, new(3));
        await session.PlayAsync();
        output.PlayedFrames = 4800;
        Assert.Equal(new MediaTime(31, 10), session.Position);
        Assert.Equal(AudioClockQuality.SYSTEM, session.ClockSnapshot.Quality);
        await session.PauseAsync();
        output.PlayedFrames = 9600;
        Assert.Equal(new MediaTime(31, 10), session.Position);
    }

    [Theory]
    [InlineData(40, 60)]
    [InlineData(-20, 120)]
    public async Task DeviceCalibrationChangesTheAudiblePosition(int delayMilliseconds, int expectedMilliseconds)
    {
        var output = new ClockAudioOutput();
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, MediaTime.Zero,
            _ => new(delayMilliseconds, 1000));
        await session.PlayAsync();
        output.PlayedFrames = 4800;
        Assert.Equal(new MediaTime(expectedMilliseconds, 1000), session.Position);
    }

    [Fact]
    public async Task SeekRebindsClockEpochAndDoesNotReuseOldFrames()
    {
        var output = new ClockAudioOutput();
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, MediaTime.Zero);
        await session.PlayAsync();
        output.PlayedFrames = 4800;
        Assert.Equal(new MediaTime(1, 10), session.Position);
        await session.SeekAsync(new(2));
        Assert.Equal(new MediaTime(2), session.Position);
        await session.PlayAsync();
        output.PlayedFrames = 2400;
        Assert.Equal(new MediaTime(205, 100), session.Position);
    }

    [Fact]
    public async Task DeviceLossFreezesLastPositionAndMakesClockUntrusted()
    {
        var output = new ClockAudioOutput();
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, MediaTime.Zero);
        await session.PlayAsync();
        output.PlayedFrames = 4800;
        Assert.Equal(new MediaTime(1, 10), session.Position);
        output.Quality = AudioClockQuality.UNAVAILABLE;
        output.PlayedFrames = 9600;
        Assert.Equal(new MediaTime(1, 10), session.Position);
        Assert.NotNull(session.Error);
        Assert.True(output.Paused);
        Assert.Equal(AudioClockQuality.UNAVAILABLE, session.ClockSnapshot.Quality);
    }

    [Fact]
    public async Task UnexpectedEpochChangeCannotUseAnotherDeviceTimeline()
    {
        var output = new ClockAudioOutput();
        await using var session = new AudioPlaybackSession(new FakeAudioSource(0), output, MediaTime.Zero);
        await session.PlayAsync();
        output.ChangeEpoch();
        Assert.Equal(MediaTime.Zero, session.Position);
        Assert.NotNull(session.Error);
    }
}
