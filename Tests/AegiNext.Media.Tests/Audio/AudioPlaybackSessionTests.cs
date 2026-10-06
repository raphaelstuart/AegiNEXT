using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Audio;

public sealed class AudioPlaybackSessionTests
{
    [Fact]
    public async Task AudioOffsetAddsSilenceAndDeviceConsumptionDrivesTheClock()
    {
        var source = new FakeAudioSource();
        var output = new FakeAudioOutput();
        await using var session = new AudioPlaybackSession(source, output, MediaTime.Zero);
        await session.PlayAsync();
        var written = output.Written;
        Assert.All(written.Take(9600), sample => Assert.Equal(0, sample));
        Assert.Contains(0.5F, written.Skip(9600));
        Assert.InRange(output.MaximumQueued, 1, 12000);
        Assert.Equal(MediaTime.Zero, session.Position);

        output.Consume(4800);
        Assert.Equal(new MediaTime(90, 1000), session.Position);
        await session.PauseAsync();
        var paused = session.Position;
        output.Consume(4800);
        Assert.Equal(paused, session.Position);
        Assert.True(output.Paused);

        await session.SeekAsync(new(2));
        Assert.Equal(new MediaTime(2), source.LastSeek);
        Assert.Equal(new MediaTime(2), session.Position);
        Assert.True(output.Paused);
        session.SetGain(0.25F);
        Assert.Equal(0.25F, output.Gain);
        await Task.WhenAll(session.CloseAsync(), session.CloseAsync());
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
    }

    [Fact]
    public async Task ShortAudioContinuesWithSilenceForALongerVideoTimeline()
    {
        var source = new FakeAudioSource(0);
        var output = new FakeAudioOutput();
        await using var session = new AudioPlaybackSession(source, output, MediaTime.Zero);
        await session.PlayAsync();
        Assert.Contains(0.5F, output.Written);
        Assert.Contains(0F, output.Written.Skip(9600));
        for (var index = 0; index < 4; index++)
        {
            output.Consume(4800);
            await session.PlayAsync();
        }

        Assert.True(session.Position > new MediaTime(3, 10));
        Assert.InRange(output.MaximumQueued, 1, 12000);
    }

    [Fact]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure", Justification = "The session is disposed and all source reads drained before the blocking event leaves scope.")]
    public async Task PauseWhilePrebufferingCannotBeOverriddenByALatePlayCompletion()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeAudioSource(0)
        {
            BeforeRead = token =>
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        var output = new FakeAudioOutput();
        await using var session = new AudioPlaybackSession(source, output, MediaTime.Zero);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var play = session.PlayAsync();
        await session.PauseAsync();
        release.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => play);
        Assert.True(output.Paused);
        Assert.Equal(MediaTime.Zero, session.Position);
    }

    [Fact]
    [SuppressMessage("ReSharper", "AccessToDisposedClosure", Justification = "The session is disposed and all source reads drained before the blocking event leaves scope.")]
    [SuppressMessage("ReSharper", "DisposeOnUsingVariable", Justification = "Concurrent close and dispose are the behavior under test; automatic disposal also covers assertion failure.")]
    public async Task SeekSubmissionOrderIsPreservedAndCloseCancelsBlockedReads()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new FakeAudioSource(0);
        var output = new FakeAudioOutput();
        await using var session = new AudioPlaybackSession(source, output, MediaTime.Zero);
        source.BeforeSeek = (target, token) =>
        {
            if (target == new MediaTime(1))
            {
                entered.TrySetResult();
                release.Wait(token);
            }
        };
        var first = session.SeekAsync(new(1));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = session.SeekAsync(new(2));
        release.Set();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new MediaTime(2), source.LastSeek);
        Assert.Equal(new MediaTime(2), session.Position);
        Assert.True(output.Paused);

        release.Reset();
        var readEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        source.BeforeRead = token =>
        {
            readEntered.TrySetResult();
            release.Wait(token);
        };
        var blocked = session.SeekAsync(new(3));
        await readEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(session.CloseAsync(), session.DisposeAsync().AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
    }

    [Fact]
    public void SampleBlocksValidateTheirShapeAndKeepIndependentMemory()
    {
        var data = new[] { 0.25F, -0.25F };
        var block = new AudioSampleBlock(new(), new(3), data);
        data[0] = 1;
        Assert.Equal(0.25F, block.Samples.Span[0]);
        Assert.Equal(1, block.FrameCount);
        Assert.Throws<ArgumentException>(() => new AudioSampleBlock(new(), MediaTime.Zero, [1F]));
        Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSampleFormat(4000, 1));
    }
}
