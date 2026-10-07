using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;

namespace AegiNext.Desktop.Tests;

public sealed class AudioPreviewGainPersistenceTests
{
    /// <summary>定位、设备重建和解码方式切换均保留静音与个人音量，并在取消静音后恢复保存音量。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SeekingReopeningAndSwitchingDecoderPreserveMuteAndSavedVolume(bool muted)
    {
        var outputs = new List<PreviewAudioOutput>();
        await using var controller = new VideoPreviewController(
            (_, _) => Task.FromResult(new VideoPreviewMedia(0, MediaTime.Zero, new(5), 1)),
            (_, _, position) => new(_ => new PreviewTestSource(10, 0, 400, 1000, 2000, 4000), externalPosition: position),
            () => new PreviewTestConverter(), Dispatch, _ => { },
            (_, _, position, _) =>
            {
                var output = new PreviewAudioOutput { ClockQuality = AudioClockQuality.SYSTEM };
                outputs.Add(output);
                return Task.FromResult(new AudioPlaybackSession(new PreviewAudioSource(), output, position));
            });
        controller.SetVolume(0.35F);
        controller.SetMuted(muted);
        await controller.OpenAsync("gain-persistence.mkv");
        var expected = muted ? 0 : 0.35F;
        Assert.Equal(expected, outputs[^1].Gain);
        await controller.PlayAsync();
        await controller.SeekAsync(new MediaTime(3, 4));
        await controller.PlayAsync();
        Assert.Equal(expected, outputs[^1].Gain);
        Assert.Equal(muted, controller.Snapshot.IsMuted);
        Assert.Equal(0.35F, controller.Snapshot.Volume);
        await controller.ReopenAudioOutputAsync();
        Assert.Equal(2, outputs.Count);
        Assert.Equal(expected, outputs[^1].Gain);
        Assert.Equal(VideoPlaybackState.PLAYING, controller.Snapshot.State);
        Assert.Null(controller.Snapshot.AudioError);
        await controller.SwitchDecodeModeAsync(VideoDecodeMode.Software);
        Assert.Equal(3, outputs.Count);
        Assert.Equal(expected, outputs[^1].Gain);
        Assert.Equal(muted, controller.Snapshot.IsMuted);
        controller.SetVolume(0.7F);
        Assert.Equal(muted ? 0 : 0.7F, outputs[^1].Gain);
        controller.SetMuted(false);
        Assert.Equal(0.7F, outputs[^1].Gain);
        await controller.CloseAsync();
        Assert.All(outputs, output => Assert.Equal(1, output.DisposeCount));
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
