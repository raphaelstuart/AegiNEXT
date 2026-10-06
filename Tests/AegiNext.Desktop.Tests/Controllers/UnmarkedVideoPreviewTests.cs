using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Tests.Rendering;
using AegiNext.Media.Audio;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class UnmarkedVideoPreviewTests
{
    [PreviewQualityNativeFact]
    public async Task UntaggedH264High720pWithAacDeliversARealPreviewFrameMatchingExplicitBt709()
    {
        using var fixture = await UnmarkedPreviewFixture.CreateAsync();
        var probe = new FfprobeMediaProbe(new(Environment.GetEnvironmentVariable("AEGINEXT_FFPROBE_PATH")!));
        var report = await probe.ProbeAsync(fixture.MediaPath);
        var video = Assert.Single(report.Asset.Streams.Where(stream => stream.Video is not null));
        Assert.Equal("h264", video.CodecName);
        Assert.Equal(1280, video.Video!.Width);
        Assert.Equal(720, video.Video.Height);
        Assert.Equal("yuv420p", video.Video.PixelFormat);
        Assert.Equal(new MediaRatio(60000, 1001), video.Video.FrameRate);
        Assert.True(video.Video.Color.Range is null or "unknown");
        Assert.True(video.Video.Color.Matrix is null or "unknown");
        Assert.True(video.Video.Color.Primaries is null or "unknown");
        Assert.True(video.Video.Color.Transfer is null or "unknown");
        var audio = Assert.Single(report.Asset.Streams.Where(stream => stream.Audio is not null));
        Assert.Equal("aac", audio.CodecName);
        Assert.Equal(44100, audio.Audio!.SampleRate);
        Assert.Equal(2, audio.Audio.Channels);

        foreach (var mode in new[] { VideoDecodeMode.Software, VideoDecodeMode.Auto })
        {
            var inferred = await ReadPresentedFrameAsync(fixture.MediaPath, mode);
            var explicitColor = await ReadPresentedFrameAsync(fixture.ReferencePath, mode);
            Assert.Equal(1280, inferred.Width);
            Assert.Equal(720, inferred.Height);
            Assert.True(inferred.Pixels.Span.SequenceEqual(explicitColor.Pixels.Span));
        }

        var softwareOptions = new VideoDecoderOptions { Mode = VideoDecodeMode.Software };
        using var decoder = FfmpegVideoDecoder.Open(fixture.MediaPath, video.Index, softwareOptions);
        using var raw = decoder.ReadFrame();
        Assert.NotNull(raw);
        Assert.Equal(0, raw.Info.ColorRangeCode);
        Assert.Equal(2, raw.Info.ColorMatrixCode);
        Assert.Equal(2, raw.Info.ColorPrimariesCode);
        Assert.Equal(2, raw.Info.ColorTransferCode);
    }

    private static async Task<SdrVideoFrame> ReadPresentedFrameAsync(string path, VideoDecodeMode mode)
    {
        var firstFrame = new TaskCompletionSource<SdrVideoFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var controller = new VideoPreviewController(VideoPreviewProbe.ProbeAsync,
            (filePath, index, clock, options) => new(token => VideoFrameNavigator.Open(filePath, index, options, token), externalPosition: clock),
            () => new SdrVideoConverter(), Dispatch, update =>
            {
                if (update.Frame is { } frame)
                {
                    firstFrame.TrySetResult(frame);
                }
                else if (update.Snapshot.Error is { } error)
                {
                    firstFrame.TrySetException(error);
                }
            },
            (_, _, position, _) => Task.FromResult(new AudioPlaybackSession(new PreviewAudioSource(), new PreviewAudioOutput(), position)));
        await controller.SwitchDecodeModeAsync(mode);
        await controller.OpenAsync(path);
        var result = await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.True(controller.Snapshot.AudioAvailable);
        Assert.Null(controller.Snapshot.Error);
        Assert.NotNull(controller.Snapshot.PresentedFrameTime);
        Assert.Equal(mode, controller.Snapshot.DecodeSessionInfo!.RequestedMode);
        if (mode == VideoDecodeMode.Software)
        {
            Assert.False(controller.Snapshot.DecodeSessionInfo.HardwareConfirmed);
        }

        return result;
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
