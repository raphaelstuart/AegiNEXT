using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class VideoPreviewRangeTests
{
    [Fact]
    public async Task LoopToggleUpdatesCurrentRangeWithoutSeekingOrStartingPausedPlayback()
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160);
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        await using var controller = CreateController(source, new(), output, clock);
        await controller.OpenAsync("toggle.mkv");
        controller.SetPlaybackRangeLoop(true);
        Assert.True(output.Paused);
        Assert.False(controller.IsRangePlaybackActive);
        Assert.False(controller.Snapshot.PlaybackRangeInstalled);
        await controller.PlayRangeAsync(MediaTime.Zero, new(20, 1000), false);
        Assert.True(controller.Snapshot.PlaybackRangeInstalled);
        var beforeToggle = source.IssuedFrames.Count;
        controller.SetPlaybackRangeLoop(true);
        Assert.Equal(beforeToggle, source.IssuedFrames.Count);
        output.Consume(960);
        await EventuallyAsync(() => source.IssuedFrames.Count > beforeToggle, clock);
        Assert.True(controller.IsRangePlaybackActive);
        controller.SetPlaybackRangeLoop(false);
        output.Consume(960);
        await EventuallyAsync(() => !controller.IsRangePlaybackActive, clock);
        Assert.True(output.Paused);
        Assert.True(controller.Snapshot.PlaybackRangeInstalled);
        var finished = source.IssuedFrames.Count;
        controller.SetPlaybackRangeLoop(true);
        clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(20);
        Assert.Equal(finished, source.IssuedFrames.Count);
        Assert.True(output.Paused);
    }

    [Fact]
    public async Task LoopRepeatsAndMainSeekRevokesThePreviousOwner()
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160);
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        await using var controller = CreateController(source, audioSource, output, clock);
        await controller.OpenAsync("loop.mkv");
        await controller.PlayRangeAsync(MediaTime.Zero, new(20, 1000), true);
        Assert.Equal(960, output.QueuedFrames);
        output.Consume(960);
        await EventuallyAsync(() => source.IssuedFrames.Count >= 3, clock);
        Assert.Equal(960, output.QueuedFrames);

        await controller.SeekAsync(new(120, 1000));
        Assert.False(controller.Snapshot.PlaybackRangeInstalled);
        var count = source.IssuedFrames.Count;
        output.Consume(48000);
        clock.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(30);

        Assert.True(output.Paused);
        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(new MediaTime(120, 1000), controller.Snapshot.Position);
        Assert.Equal(count, source.IssuedFrames.Count);
        Assert.Null(controller.Snapshot.Error);
    }

    [Fact]
    public async Task CancellingTheRangeTokenPausesAudioAndVideoWithoutClosingMedia()
    {
        var source = new PreviewTestSource(10, 0, 40, 100);
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        await using var controller = CreateController(source, new(), output, clock);
        await controller.OpenAsync("cancel.mkv");
        using var cancellation = new CancellationTokenSource();
        await controller.PlayRangeAsync(MediaTime.Zero, new(1, 10), true, cancellation.Token);
        await cancellation.CancelAsync();
        await EventuallyAsync(() => output.Paused && controller.Snapshot.State == VideoPlaybackState.PAUSED, clock);

        Assert.NotNull(controller.MediaInfo);
        await controller.ClearPlaybackRangeAsync();
        await controller.PlayAsync();
        Assert.False(output.Paused);
        Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
    }

    [Fact]
    public async Task CancellingAfterAudioStartsBeforeVideoStartsStillPausesAudio()
    {
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        await using var controller = CreateController(new(10, 0, 40, 100), new(), output, clock);
        await controller.OpenAsync("cancel-start.mkv");
        using var cancellation = new CancellationTokenSource();
        output.PlaybackStarted = cancellation.Cancel;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.PlayRangeAsync(MediaTime.Zero, new(1, 10), true, cancellation.Token));
        await EventuallyAsync(() => output.Paused && !controller.IsRangePlaybackActive, clock);

        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Null(controller.Snapshot.Error);
        output.PlaybackStarted = null;
        await controller.ClearPlaybackRangeAsync();
        await controller.PlayAsync();
        Assert.False(output.Paused);
    }

    [Fact]
    public async Task DevicePauseFailureStillClosesAudioAndVideoResources()
    {
        var source = new PreviewTestSource(10, 0, 40, 100);
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(source, audioSource, output, new());
        await controller.OpenAsync("failing-device.mkv");
        await controller.PlayRangeAsync(MediaTime.Zero, new(1, 10), true);
        output.ThrowOnPause = true;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            controller.CloseMediaAsync().WaitAsync(TimeSpan.FromSeconds(5)));

        Assert.Equal(1, audioSource.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(VideoPlaybackState.CREATED, controller.Snapshot.State);
        Assert.False(controller.IsRangePlaybackActive);
    }

    [Fact]
    public async Task MediaBoundsClipTheRequestedRangeAndClearLeavesPlaybackPaused()
    {
        var source = new PreviewTestSource(10, 3000, 3040, 3100, 3990);
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        await using var controller = CreateController(source, audioSource, output, clock, new(3));
        await controller.OpenAsync("offset.mkv");
        await controller.PlayRangeAsync(new(399, 100), new(5), false);
        Assert.Equal(new MediaTime(399, 100), audioSource.LastSeek);
        Assert.Equal(480, output.QueuedFrames);
        output.Consume(480);
        await EventuallyAsync(() => controller.Snapshot.State == VideoPlaybackState.ENDED, clock);
        Assert.Equal(new MediaTime(4), controller.Snapshot.Position);
        Assert.True(output.Paused);
        Assert.True(controller.Snapshot.PlaybackRangeInstalled);

        await controller.ClearPlaybackRangeAsync();
        Assert.False(controller.Snapshot.PlaybackRangeInstalled);
        Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.True(output.Paused);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => controller.PlayRangeAsync(new(5), new(6), true));
    }

    [Fact]
    public async Task EndedRangeRemainsMarkedUntilSeekingTheSamePositionRestoresUnboundedPlayback()
    {
        var source = new PreviewTestSource(10, 3000, 3040, 3100, 3600, 3990);
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        await using var controller = CreateController(source, audioSource, output, clock, new(3));
        await controller.OpenAsync("resume-range.mkv");
        Assert.False(controller.Snapshot.PlaybackRangeInstalled);
        await controller.PlayRangeAsync(new(304, 100), new(31, 10), false);
        Assert.True(controller.Snapshot.PlaybackRangeInstalled);
        output.Consume(48000);
        await EventuallyAsync(() => controller.Snapshot.State == VideoPlaybackState.ENDED &&
            !controller.IsRangePlaybackActive && output.Paused, clock);
        var end = controller.Snapshot;
        Assert.Equal(new MediaTime(31, 10), end.Position);
        Assert.True(end.PlaybackRangeInstalled);

        await controller.SeekAsync(end.Position);
        await controller.PlayAsync();

        Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
        Assert.False(controller.Snapshot.PlaybackRangeInstalled);
        Assert.Equal(end.Position, controller.Snapshot.Position);
        Assert.Equal(end.Position, audioSource.LastSeek);
        Assert.False(output.Paused);
        Assert.True(output.QueuedFrames > 2880);
        await controller.CloseMediaAsync();
        Assert.False(controller.Snapshot.PlaybackRangeInstalled);
    }

    [Fact]
    public async Task ClosingRangePlaybackCancelsTheLoopAndReleasesAllResources()
    {
        var source = new PreviewTestSource(10, 0, 40, 100);
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput();
        await using var controller = CreateController(source, audioSource, output, new());
        await controller.OpenAsync("close.mkv");
        await controller.PlayRangeAsync(MediaTime.Zero, new(1, 10), true);
        await controller.CloseMediaAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, audioSource.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(VideoPlaybackState.CREATED, controller.Snapshot.State);
        Assert.False(controller.Snapshot.PlaybackRangeInstalled);
    }

    private static VideoPreviewController CreateController(PreviewTestSource source, PreviewAudioSource audioSource,
        PreviewAudioOutput output, ManualPlaybackTimeProvider clock, MediaTime? start = null)
    {
        return new((_, _) => Task.FromResult(new VideoPreviewMedia(0, start ?? MediaTime.Zero, new(1), 1)),
            (_, _, position) => new(_ => source, clock, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, _ => { },
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(audioSource, output, position)));
    }

    private static Task Dispatch(Action action, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }

    private static async Task EventuallyAsync(Func<bool> predicate, ManualPlaybackTimeProvider clock)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!predicate())
        {
            clock.Advance(TimeSpan.FromMilliseconds(5));
            await Task.Delay(5, timeout.Token);
        }
    }
}
