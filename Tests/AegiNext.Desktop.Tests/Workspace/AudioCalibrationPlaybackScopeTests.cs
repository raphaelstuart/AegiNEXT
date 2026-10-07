using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Settings.Media;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AudioCalibrationPlaybackScopeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ChangingTheOutputPreservesTheBoundedVideoRangeItsOwnerAndOriginalLoopStart(bool reopen, bool loop)
    {
        var firstSource = new CalibrationAudioSource();
        var nextSource = new CalibrationAudioSource();
        var firstOutput = new CalibrationAudioOutput("speaker");
        var nextOutput = new CalibrationAudioOutput("headphones");
        var video = new PreviewTestSource(10, 0, 1000, 2000, 5000, 10000, 16000);
        var calls = 0;
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => video, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, target, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1
                ? new AudioPlaybackSession(firstSource, firstOutput, target)
                : new AudioPlaybackSession(nextSource, nextOutput, target))));
        await context.InitializeAsync();
        var controller = context.Session.Controller;
        await controller.OpenAsync("bounded-output-change.mkv");
        using var owner = new CancellationTokenSource();
        var start = new MediaTime(1);
        var end = new MediaTime(5, 4);
        await controller.PlayRangeAsync(start, end, loop, owner.Token);
        firstOutput.Consume(2400);
        var target = controller.Snapshot.Position;
        Assert.Equal(new MediaTime(21, 20), target);

        await ChangeOutputAsync(context, firstOutput, reopen);

        var output = reopen ? nextOutput : firstOutput;
        var source = reopen ? nextSource : firstSource;
        Assert.Null(context.Session.LastError);
        Assert.Equal(target, controller.Snapshot.Position);
        Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
        Assert.True(controller.Snapshot.PlaybackRangeInstalled);
        Assert.True(controller.IsPlaybackRangeOwnedBy(owner.Token));
        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(target, source.SeekTargets.Last());
        var seeks = source.SeekTargets.Count;
        output.Consume(output.QueuedFrames);
        if (loop)
        {
            await EventuallyAsync(() => source.SeekTargets.Count > seeks && source.SeekTargets.Last() == start &&
                controller.Snapshot.State == VideoPlaybackState.PLAYING && !output.Paused);
            Assert.True(controller.IsPlaybackRangeOwnedBy(owner.Token));
            Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
        }
        else
        {
            await EventuallyAsync(() => output.Paused && controller.Snapshot.State == VideoPlaybackState.ENDED &&
                !controller.IsRangePlaybackActive);
            Assert.Equal(end, controller.Snapshot.Position);
            Assert.False(controller.IsRangePlaybackActive);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewSeekOrCancelledOwnerDiscardsALateOutputAndCannotRestoreTheOldRange(bool cancelOwner)
    {
        var firstSource = new CalibrationAudioSource();
        var nextSource = new CalibrationAudioSource();
        var firstOutput = new CalibrationAudioOutput("speaker");
        var nextOutput = new CalibrationAudioOutput("headphones");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 1000, 2000, 5000, 10000, 16000), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            async (_, _, target, _) =>
            {
                if (Interlocked.Increment(ref calls) == 1)
                {
                    return new(firstSource, firstOutput, target);
                }
                var replacement = new AudioPlaybackSession(nextSource, nextOutput, target);
                entered.TrySetResult();
                await release.Task;
                return replacement;
            }));
        try
        {
            await context.InitializeAsync();
            var controller = context.Session.Controller;
            await controller.OpenAsync("superseded-output-change.mkv");
            using var owner = new CancellationTokenSource();
            await controller.PlayRangeAsync(new(1), new(5, 4), true, owner.Token);
            firstOutput.Consume(2400);
            var target = controller.Snapshot.Position;
            var changing = controller.ReopenAudioOutputAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (cancelOwner)
            {
                await owner.CancelAsync();
            }
            else
            {
                target = new(3);
                await controller.SeekAsync(target).WaitAsync(TimeSpan.FromSeconds(5));
            }
            release.TrySetResult();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changing.WaitAsync(TimeSpan.FromSeconds(5)));

            Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
            Assert.Equal(target, controller.Snapshot.Position);
            Assert.False(controller.IsRangePlaybackActive);
            Assert.False(controller.IsPlaybackRangeOwnedBy(owner.Token));
            Assert.Equal(1, nextSource.DisposeCount);
            Assert.Equal(1, nextOutput.DisposeCount);
            Assert.Equal(1, firstSource.DisposeCount);
            Assert.Equal(1, firstOutput.DisposeCount);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingTheOutputPreservesAudioOnlyAuditionItsAudioPositionAndFrozenVideoFrame(bool reopen)
    {
        var firstSource = new CalibrationAudioSource();
        var nextSource = new CalibrationAudioSource();
        var firstOutput = new CalibrationAudioOutput("speaker");
        var nextOutput = new CalibrationAudioOutput("headphones");
        var video = new PreviewTestSource(10, 0, 1000, 2000, 5000, 10000, 16000);
        var calls = 0;
        await using var context = new WorkspaceSessionTestContext(controllerFactory: update => new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(20), 1)),
            (_, _, position) => new(_ => video, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, update,
            (_, _, target, _) => Task.FromResult(Interlocked.Increment(ref calls) == 1
                ? new AudioPlaybackSession(firstSource, firstOutput, target)
                : new AudioPlaybackSession(nextSource, nextOutput, target))));
        await context.InitializeAsync();
        var controller = context.Session.Controller;
        await controller.OpenAsync("audio-only-output-change.mkv");
        await controller.SeekAsync(new(5000001, 1000000));
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(5));
        var frozen = controller.Snapshot;
        var decoded = video.IssuedFrames.Count;
        using var owner = new CancellationTokenSource();
        await controller.PlayAudioRangeAsync(new(1), new(5, 4), owner.Token);
        firstOutput.Consume(2400);
        Assert.Equal(AudioClockQuality.SYSTEM, context.Session.AudioClock!.Quality);
        Assert.Equal(2400, context.Session.AudioClock.PlayedFrames);

        await ChangeOutputAsync(context, firstOutput, reopen);

        var output = reopen ? nextOutput : firstOutput;
        var source = reopen ? nextSource : firstSource;
        Assert.Null(context.Session.LastError);
        Assert.True(controller.IsPlaybackRangeOwnedBy(owner.Token));
        Assert.True(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(new MediaTime(21, 20), source.SeekTargets.Last());
        Assert.False(output.Paused);
        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(frozen.Position, controller.Snapshot.Position);
        Assert.Equal(frozen.PresentedFrameTime, controller.Snapshot.PresentedFrameTime);
        Assert.Equal(frozen.PresentedGeneration, controller.Snapshot.PresentedGeneration);
        Assert.Equal(decoded, video.IssuedFrames.Count);
        output.Consume(output.QueuedFrames);
        await EventuallyAsync(() => output.Paused && !controller.IsRangePlaybackActive);
        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(frozen.Position, controller.Snapshot.Position);
        Assert.Equal(frozen.PresentedFrameTime, controller.Snapshot.PresentedFrameTime);
        Assert.Equal(frozen.PresentedGeneration, controller.Snapshot.PresentedGeneration);
        Assert.Equal(decoded, video.IssuedFrames.Count);
    }

    private static async Task ChangeOutputAsync(WorkspaceSessionTestContext context, CalibrationAudioOutput output, bool reopen)
    {
        if (reopen)
        {
            output.Quality = AudioClockQuality.UNAVAILABLE;
            context.Session.Tick();
            await context.Session.AudioCalibrationCompletion.WaitAsync(TimeSpan.FromSeconds(5));
        }
        else
        {
            await context.Session.SetAudioCalibrationAsync(new("speaker", "test-system", 48000, 2, 85));
        }
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
