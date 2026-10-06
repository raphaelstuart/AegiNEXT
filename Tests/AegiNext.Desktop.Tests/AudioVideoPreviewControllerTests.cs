using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests;

public sealed class AudioVideoPreviewControllerTests
{
    [Fact]
    public async Task AudioConsumptionDrivesVideoAndSeekPauseVolumeAndEndStayCoordinated()
    {
        var source = new PreviewTestSource(10, 0, 40, 100, 160);
        var audioSource = new PreviewAudioSource();
        var output = new PreviewAudioOutput();
        var clock = new ManualPlaybackTimeProvider();
        var updates = new ConcurrentQueue<VideoPreviewUpdate>();
        await using var controller = new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1), 1)),
            (_, _, position) => new(_ => source, clock, externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, updates.Enqueue,
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(audioSource, output, position)));
        await controller.OpenAsync("audio.mkv");
        Assert.True(controller.Snapshot.AudioAvailable);
        controller.SetVolume(0.3F);
        Assert.Equal(0.3F, output.Gain);
        controller.SetMuted(true);
        Assert.Equal(0, output.Gain);
        controller.SetMuted(false);
        Assert.Equal(0.3F, output.Gain);
        await controller.PlayAsync();
        output.Consume(2400);
        clock.Advance(TimeSpan.FromMilliseconds(40));
        Assert.Equal(new MediaTime(40, 1000), controller.Snapshot.Position);
        await controller.PauseAsync();
        Assert.True(output.Paused);
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(new MediaTime(40, 1000), controller.Snapshot.Position);
        await controller.SeekAsync(new(110, 1000));
        Assert.Equal(new MediaTime(110, 1000), audioSource.LastSeek);
        Assert.Equal(new MediaTime(110, 1000), controller.Snapshot.Position);
        await controller.PlayAsync();
        output.Consume(4800);
        clock.Advance(TimeSpan.FromSeconds(1));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (controller.Snapshot.State != VideoPlaybackState.ENDED || !output.Paused)
        {
            await Task.Delay(1, timeout.Token);
            clock.Advance(TimeSpan.FromMilliseconds(50));
        }

        Assert.Equal(new MediaTime(160, 1000), controller.Snapshot.Position);
        await controller.CloseMediaAsync();
        Assert.Equal(1, audioSource.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
        Assert.False(controller.Snapshot.AudioAvailable);
    }

    [Fact]
    public async Task MissingOutputDeviceReportsAudioFailureWhileVideoRemainsUsable()
    {
        var failure = new IOException("No audio device");
        await using var controller = new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(1), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 40, 100), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, _ => { },
            (_, _, _, _) => Task.FromException<AudioPlaybackSession>(failure));
        await controller.OpenAsync("silent-device.mkv");
        Assert.Same(failure, controller.Snapshot.AudioError);
        Assert.False(controller.Snapshot.AudioAvailable);
        await controller.SeekAsync(new(50, 1000));
        Assert.Equal(new MediaTime(50, 1000), controller.Snapshot.Position);
        await controller.PlayAsync();
        await controller.PauseAsync();
        Assert.Null(controller.Snapshot.Error);
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
