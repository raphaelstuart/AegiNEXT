using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class VideoPreviewAudioRangeTests
{
    [Fact]
    public async Task AudioRangeFreezesVideoPositionAndPresentedFrameAndTrimsExclusivePcmSamples()
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 500);
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, audioSource, output, updates.Enqueue);
        await controller.OpenAsync("frozen.mkv");
        await controller.SeekAsync(new(120, 1000));
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(100, 1000));
        var frozen = controller.Snapshot;
        var decodedCount = source.IssuedFrames.Count;
        var range = new MediaTimeRange(new(2, 48000), new(13, 96000));
        using var cancellation = new CancellationTokenSource();

        await controller.PlayAudioRangeAsync(range.Start, range.End, cancellation.Token);

        Assert.True(controller.IsPlaybackRangeOwnedBy(cancellation.Token));
        Assert.True(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(range.Start, audioSource.LastSeek);
        Assert.Equal(new float[] { 2, 2, 3, 3, 4, 4, 5, 5, 6, 6 }, output.Written);
        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(frozen.Position, controller.Snapshot.Position);
        Assert.Equal(frozen.PresentedFrameTime, controller.Snapshot.PresentedFrameTime);
        Assert.Equal(frozen.PresentedGeneration, controller.Snapshot.PresentedGeneration);

        output.Consume(48000);
        await EventuallyAsync(() => !controller.IsRangePlaybackActive && output.Paused);

        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(decodedCount, source.IssuedFrames.Count);
        Assert.Equal(frozen.Position, controller.Snapshot.Position);
        Assert.Equal(frozen.PresentedFrameTime, controller.Snapshot.PresentedFrameTime);
        Assert.Null(controller.Snapshot.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClearingAudioAuditionPreservesPausedVideoPositionAndPresentationMetadata(bool ownedClear)
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 500, 800);
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = CreateController(source, audioSource, output, updates.Enqueue);
        Assert.False(controller.Snapshot.AudioAuditionActive);
        await controller.OpenAsync("clear-preserves-frame.mkv");
        await controller.SeekAsync(new(120, 1000));
        await EventuallyAsync(() => controller.Snapshot.PresentedFrameTime == new MediaTime(100, 1000));
        var frozen = controller.Snapshot;
        var decodedCount = source.IssuedFrames.Count;
        using var owner = new CancellationTokenSource();
        await controller.PlayAudioRangeAsync(new(1, 2), new(3, 4), owner.Token);
        Assert.True(controller.Snapshot.AudioAuditionActive);
        Assert.Contains(updates, update => update.Snapshot.AudioAuditionActive);

        if (ownedClear)
        {
            await controller.ClearPlaybackRangeAsync(owner.Token);
        }
        else
        {
            await controller.ClearPlaybackRangeAsync();
        }

        var stopped = controller.Snapshot;
        Assert.False(stopped.AudioAuditionActive);
        Assert.False(controller.IsRangePlaybackActive);
        Assert.True(output.Paused);
        Assert.Equal(frozen.Position, stopped.Position);
        Assert.Equal(frozen.Position, audioSource.LastSeek);
        Assert.Equal(frozen.PresentedFrameTime, stopped.PresentedFrameTime);
        Assert.Equal(frozen.PresentedAtPosition, stopped.PresentedAtPosition);
        Assert.Equal(frozen.PresentedGeneration, stopped.PresentedGeneration);
        Assert.Equal(decodedCount, source.IssuedFrames.Count);
        Assert.False(updates.Last().Snapshot.AudioAuditionActive);
        Assert.Equal(frozen.PresentedGeneration, updates.Last().Snapshot.PresentedGeneration);
    }

    [Fact]
    public async Task AudioAuditionFlagTracksReplacementWithVideoPlaybackAndMediaClosure()
    {
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(new(10, 0, 40, 100, 160, 500, 800), new(), output);
        await controller.OpenAsync("flag-lifetime.mkv");
        await controller.PlayAudioRangeAsync(new(1, 2), new(3, 4));
        Assert.True(controller.Snapshot.AudioAuditionActive);

        await controller.PlayRangeAsync(MediaTime.Zero, new(1, 10), true);
        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.True(controller.IsRangePlaybackActive);
        Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
        await controller.PlayAudioRangeAsync(new(1, 2), new(3, 4));
        Assert.True(controller.Snapshot.AudioAuditionActive);
        await controller.CloseMediaAsync();

        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.False(controller.Snapshot.AudioAvailable);
        Assert.Equal(VideoPlaybackState.CREATED, controller.Snapshot.State);
        await controller.CloseAsync();
        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(VideoPlaybackState.CLOSED, controller.Snapshot.State);
    }

    [Theory]
    [InlineData(2999, 3001, 3000, 48)]
    [InlineData(3999, 4100, 3999, 48)]
    public async Task AudioRangeClipsAtOriginalMediaOriginAndDuration(int startMs, int endMs, int seekMs, int frames)
    {
        var source = new PreviewTestSource(10, 3000, 3040, 3100, 3990);
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(source, audioSource, output, start: new(3));
        await controller.OpenAsync("offset.mkv");
        var decodedCount = source.IssuedFrames.Count;

        await controller.PlayAudioRangeAsync(new(startMs, 1000), new(endMs, 1000));

        Assert.Equal(new MediaTime(seekMs, 1000), audioSource.LastSeek);
        Assert.Equal(frames, output.QueuedFrames);
        Assert.Equal(seekMs * 48, output.Written[0]);
        Assert.Equal(seekMs * 48 + frames - 1, output.Written[^1]);
        Assert.Equal(new MediaTime(3), controller.Snapshot.Position);
        Assert.Equal(decodedCount, source.IssuedFrames.Count);
    }

    [Fact]
    public async Task ReplacementDuringAudioSeekCancelsOldStartAndOldOwnerCannotClearOrLoopNewRange()
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 500, 800);
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(source, audioSource, output);
        await controller.OpenAsync("replace.mkv");
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        audioSource.BeforeSeek = target =>
        {
            if (target == new MediaTime(1, 10))
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
        };
        var oldStart = controller.PlayAudioRangeAsync(new(1, 10), new(11, 100), first.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var newStart = controller.PlayAudioRangeAsync(new(1, 2), new(51, 100), second.Token);
            await first.CancelAsync();
            await controller.ClearPlaybackRangeAsync(first.Token);
            controller.SetPlaybackRangeLoop(true, first.Token);
            release.Set();
            await newStart.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldStart);

            Assert.True(controller.IsPlaybackRangeOwnedBy(second.Token));
            Assert.True(controller.Snapshot.AudioAuditionActive);
            Assert.False(controller.IsPlaybackRangeOwnedBy(first.Token));
            Assert.Equal(new MediaTime(1, 2), audioSource.LastSeek);
            Assert.Equal(480, output.QueuedFrames);
            Assert.Equal(24000, output.Written[0]);
            Assert.False(output.Paused);
            Assert.Equal(MediaTime.Zero, controller.Snapshot.Position);

            output.Consume(480);
            await EventuallyAsync(() => !controller.IsRangePlaybackActive && output.Paused);
            Assert.Equal(new MediaTime(1, 2), audioSource.LastSeek);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task StartingAuditionPausesRunningVideoAtItsCurrentPosition()
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 500, 800);
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(source, audioSource, output);
        await controller.OpenAsync("playing.mkv");
        await controller.PlayAsync();
        output.Consume(4320);
        Assert.Equal(new MediaTime(80, 1000), controller.Snapshot.Position);

        await controller.PlayAudioRangeAsync(new(1, 2), new(3, 4));

        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(new MediaTime(80, 1000), controller.Snapshot.Position);
        Assert.Equal(new MediaTime(1, 2), audioSource.LastSeek);
        Assert.False(output.Paused);
        var count = source.IssuedFrames.Count;
        output.Consume(9600);
        await Task.Delay(25);
        Assert.Equal(new MediaTime(80, 1000), controller.Snapshot.Position);
        Assert.Equal(count, source.IssuedFrames.Count);
    }

    [Fact]
    public async Task NewAuditionDuringOldClearKeepsItsOwnRangeAndDoesNotFailOldClear()
    {
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(new(10, 0, 40, 100, 160, 500, 800), audioSource, output);
        await controller.OpenAsync("clear-race.mkv");
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        await controller.PlayAudioRangeAsync(new(1, 10), new(11, 100), first.Token);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        audioSource.BeforeSeek = target =>
        {
            if (target == new MediaTime(1, 10))
            {
                entered.Set();
                Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            }
        };
        var oldClear = controller.ClearPlaybackRangeAsync(first.Token);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        try
        {
            var newStart = controller.PlayAudioRangeAsync(new(1, 2), new(51, 100), second.Token);
            release.Set();
            await Task.WhenAll(oldClear, newStart).WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(controller.IsPlaybackRangeOwnedBy(second.Token));
            Assert.Equal(new MediaTime(1, 2), audioSource.LastSeek);
            Assert.Equal(480, output.QueuedFrames);
            Assert.False(output.Paused);
            Assert.Equal(MediaTime.Zero, controller.Snapshot.Position);
        }
        finally
        {
            release.Set();
        }
    }

    [Fact]
    public async Task CancelledAudioRangeStopsWithoutMovingOrDecodingVideo()
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 500, 800);
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(source, new(), output);
        await controller.OpenAsync("cancel.mkv");
        await controller.SeekAsync(new(120, 1000));
        var count = source.IssuedFrames.Count;
        using var cancellation = new CancellationTokenSource();
        await controller.PlayAudioRangeAsync(new(1, 2), new(3, 4), cancellation.Token);
        Assert.True(controller.Snapshot.AudioAuditionActive);
        await cancellation.CancelAsync();
        await EventuallyAsync(() => !controller.IsRangePlaybackActive && output.Paused);

        Assert.Equal(new MediaTime(120, 1000), controller.Snapshot.Position);
        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(count, source.IssuedFrames.Count);
        Assert.NotNull(controller.MediaInfo);
        Assert.Null(controller.Snapshot.Error);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task NormalPlaybackAndSeekRevokeAuditionAndRestoreAudioVideoPosition(bool play)
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160, 500, 800);
        var audioSource = new PreviewRangeAudioSource();
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(source, audioSource, output);
        await controller.OpenAsync("resume.mkv");
        await controller.SeekAsync(new(120, 1000));
        using var owner = new CancellationTokenSource();
        await controller.PlayAudioRangeAsync(new(1, 2), new(3, 4), owner.Token);

        if (play)
        {
            await controller.PlayAsync();
        }
        else
        {
            await controller.SeekAsync(new(140, 1000));
        }
        await controller.ClearPlaybackRangeAsync(owner.Token);

        var expected = new MediaTime(play ? 120 : 140, 1000);
        Assert.False(controller.IsRangePlaybackActive);
        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.Equal(expected, audioSource.LastSeek);
        Assert.Equal(expected, controller.Snapshot.Position);
        Assert.Equal(play ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(!play, output.Paused);
        Assert.True(output.QueuedFrames > 480);
    }

    [Fact]
    public async Task MissingAudioRejectsAuditionAndLeavesVideoUsable()
    {
        await using var controller = new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1))),
            (_, _) => new(_ => new PreviewTestSource(10, 0, 40, 100)),
            () => new PreviewTestConverter(), Dispatch, _ => { });
        await controller.OpenAsync("no-audio.mkv");

        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.PlayAudioRangeAsync(MediaTime.Zero, new(1, 10)));

        Assert.False(controller.IsRangePlaybackActive);
        Assert.False(controller.Snapshot.AudioAuditionActive);
        Assert.Null(controller.Snapshot.Error);
        await controller.SeekAsync(new(50, 1000));
        Assert.Equal(new MediaTime(50, 1000), controller.Snapshot.Position);
    }

    internal static VideoPreviewController CreateController(PreviewTestSource source, PreviewRangeAudioSource audioSource,
        PreviewAudioOutput output, Action<VideoPreviewUpdate>? present = null, MediaTime? start = null)
    {
        return new((_, _) => Task.FromResult(new VideoPreviewMedia(0, start ?? MediaTime.Zero, new(1), 1)),
            (_, _, position) => new(_ => source, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, present ?? (_ => { }),
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(audioSource, output, position)));
    }

    private static Task Dispatch(Action action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
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
