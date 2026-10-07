using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests.Controllers;

/// <summary>验证定位事务与预览刷新、后续播放命令之间的所有权。</summary>
public sealed class VideoPreviewTransportSeekTests
{
    /// <summary>画质刷新只使旧画面失效，不能取消正在完成的媒体定位。</summary>
    [Fact]
    public async Task QualityInvalidationDoesNotCancelThePendingMediaSeek()
    {
        var frames = new PreviewTestSource(10, 0, 1000, 2000, 3000, 4000, 5000);
        var source = new BlockingPreviewSeekSource(frames);
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var controller = CreateController(source, output);
        await controller.OpenAsync("seek-quality.mkv");
        source.BlockNextSeek();
        var seek = controller.SeekAsync(new(2));
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.InvalidatePreview();
            source.Release();
            await seek.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(new MediaTime(2), controller.Snapshot.Position);
            Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
            Assert.Null(controller.Snapshot.Error);
            Assert.Null(controller.Snapshot.AudioError);
        }
        finally
        {
            source.Release();
        }
    }

    /// <summary>播放定位同时保留暂停或播放意图以及实际输出增益。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task TransportSeekKeepsTheRequestedPlayingStateAndGain(bool playing, bool muted)
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 1000, 2000, 3000, 4000, 5000));
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var controller = CreateController(source, output);
        await controller.OpenAsync("seek-state.mkv");
        controller.SetVolume(0.35F);
        controller.SetMuted(muted);
        if (playing)
        {
            await controller.PlayAsync();
        }
        await controller.SeekForPlaybackAsync(new(2), playing);

        Assert.Equal(playing ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED, controller.Snapshot.State);
        Assert.Equal(new MediaTime(2), controller.Snapshot.Position);
        Assert.Equal(!playing, output.Paused);
        Assert.Equal(muted ? 0 : 0.35F, output.Gain);
        output.Consume(2400);
        Assert.Equal(playing ? new MediaTime(41, 20) : new(2), controller.Snapshot.Position);
        Assert.Null(controller.Snapshot.AudioError);
    }

    /// <summary>拖动期间切换画质不能覆盖正在恢复播放的定位请求。</summary>
    [Fact]
    public async Task QualityRefreshDuringABlockedPlayingSeekWaitsWithoutSubmittingAPausedSeek()
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 1000, 2000, 3000, 4000, 5000));
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var controller = CreateController(source, output);
        await controller.OpenAsync("playing-seek-quality.mkv");
        await controller.PlayAsync();
        source.BlockNextSeek();
        var seek = controller.SeekForPlaybackAsync(new(2), true);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            controller.InvalidatePreview();
            var refresh = controller.RefreshPausedPreviewAsync();
            source.Release();
            await Task.WhenAll(seek, refresh).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
            Assert.False(output.Paused);
            Assert.Equal(1, source.SeekTargets.Count(target => target == new MediaTime(2)));
            output.Consume(2400);
            Assert.Equal(new MediaTime(41, 20), controller.Snapshot.Position);
        }
        finally
        {
            source.Release();
        }
    }

    /// <summary>新的暂停拥有控制权，不能被旧定位迟到的恢复阶段重新播放。</summary>
    [Fact]
    public async Task ANewerPausePreventsAnOlderBlockedSeekFromResumingAudioOrVideo()
    {
        var source = new BlockingPreviewSeekSource(new(10, 0, 1000, 2000, 3000, 4000, 5000));
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        var starts = 0;
        output.PlaybackStarted = () => Interlocked.Increment(ref starts);
        await using var controller = CreateController(source, output);
        await controller.OpenAsync("seek-pause.mkv");
        await controller.PlayAsync();
        source.BlockNextSeek();
        var seek = controller.SeekForPlaybackAsync(new(2), true);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var pause = controller.PauseAsync();
            source.Release();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seek.WaitAsync(TimeSpan.FromSeconds(5)));
            await pause.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(VideoPlaybackState.PAUSED, controller.Snapshot.State);
            Assert.True(output.Paused);
            Assert.Equal(1, starts);
        }
        finally
        {
            source.Release();
        }
    }

    /// <summary>最新定位的目标和播放意图一起替换旧定位。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ANewerSeekOwnsBothTheTargetAndThePlayingIntent(bool resume)
    {
        var frames = new PreviewTestSource(10, 0, 1000, 2000, 3000, 4000, 5000);
        var source = new BlockingPreviewSeekSource(frames);
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var controller = CreateController(source, output);
        await controller.OpenAsync("seek-latest.mkv");
        await controller.PlayAsync();
        source.BlockNextSeek();
        var first = controller.SeekForPlaybackAsync(new(2), true);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var latest = controller.SeekForPlaybackAsync(new(4), resume);
            source.Release();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first.WaitAsync(TimeSpan.FromSeconds(5)));
            await latest.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new MediaTime(4), controller.Snapshot.Position);
            Assert.Equal(resume ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED, controller.Snapshot.State);
            Assert.Equal(!resume, output.Paused);
        }
        finally
        {
            source.Release();
        }
        await controller.CloseMediaAsync();
        Assert.Equal(1, frames.DisposeCount);
        Assert.All(frames.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
    }

    /// <summary>关闭时取消尚未完成的定位，旧恢复阶段不能重新打开音视频输出。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosingDuringABlockedSeekCannotResumeTheRetiredMedia(bool closeController)
    {
        var frames = new PreviewTestSource(10, 0, 1000, 2000, 3000, 4000, 5000);
        var source = new BlockingPreviewSeekSource(frames);
        var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
        await using var controller = CreateController(source, output);
        await controller.OpenAsync("seek-close.mkv");
        await controller.PlayAsync();
        source.BlockNextSeek();
        var seek = controller.SeekForPlaybackAsync(new(2), true);
        try
        {
            await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var close = closeController ? controller.CloseAsync() : controller.CloseMediaAsync();
            await close.WaitAsync(TimeSpan.FromSeconds(5));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => seek.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(closeController ? VideoPlaybackState.CLOSED : VideoPlaybackState.CREATED, controller.Snapshot.State);
            Assert.False(controller.Snapshot.AudioAvailable);
            Assert.True(output.Paused);
            Assert.Equal(1, output.DisposeCount);
            Assert.Equal(1, frames.DisposeCount);
            Assert.All(frames.IssuedFrames, frame => Assert.Equal(1, frame.DisposeCount));
        }
        finally
        {
            source.Release();
        }
    }

    private static VideoPreviewController CreateController(BlockingPreviewSeekSource source, PreviewAudioOutput output)
    {
        return new((_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(6), 1)),
            (_, _, clock) => new(_ => source, externalPosition: clock),
            () => new PreviewTestConverter(), Dispatch, _ => { },
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(new PreviewAudioSource(), output, position)));
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
