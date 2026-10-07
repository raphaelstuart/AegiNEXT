using System.Diagnostics;
using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Tests.Rendering;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;
using AegiNext.Media.Preview;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Tests.Controllers;

[Collection("Native preview timing")]
public sealed class NativeContinuousPreviewTests
{
    [PreviewQualityNativeFact]
    public async Task RealSoftwareAutoAndHardwareKeepPresentingValid1080P60FramesAndReleaseAllNativeResources()
    {
        using var fixture = await NativeContinuousPreviewFixture.CreateAsync();
        var probe = new FfprobeMediaProbe(new(MediaToolchain.ResolveFfprobe()));
        var report = await probe.ProbeAsync(fixture.MediaPath);
        var video = Assert.Single(report.Asset.Streams.Where(stream => stream.Video is not null));
        Assert.Equal("h264", video.CodecName);
        Assert.Equal(1920, video.Video!.Width);
        Assert.Equal(1080, video.Video.Height);
        Assert.Equal(new MediaRatio(60, 1), video.Video.FrameRate);
        Assert.Equal("bt709", video.Video.Color.Matrix);
        Assert.Equal("bt709", video.Video.Color.Primaries);
        Assert.Equal("bt709", video.Video.Color.Transfer);
        Assert.DoesNotContain(report.Asset.Streams, stream => stream.Audio is not null);
        var modes = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows()
            ? new[] { VideoDecodeMode.Software, VideoDecodeMode.Auto, VideoDecodeMode.Hardware }
            : [VideoDecodeMode.Software, VideoDecodeMode.Auto];
        foreach (var mode in modes)
        {
            await VerifyModeAsync(fixture.MediaPath, mode);
        }
    }

    private static async Task VerifyModeAsync(string path, VideoDecodeMode mode)
    {
        var initialResources = (Decoders: FfmpegVideoDecoder.GetLiveDecoderCount(),
            Frames: FfmpegVideoDecoder.GetLiveFrameCount(), Converters: SdrVideoConverter.LiveConverterCount);
        var metrics = new NativePreviewTimingMetrics();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        MeasuredNativePreviewSource? source = null;
        MeasuredNativePreviewConverter? converter = null;
        VideoPreviewSnapshot? endingSnapshot = null;
        string? failure = null;
        await using var controller = new VideoPreviewController(VideoPreviewProbe.ProbeAsync,
            (filePath, index, clock, options) => new(token =>
            {
                source = new(VideoFrameNavigator.Open(filePath, index, options, token), metrics);
                return source;
            }, TimeProvider.System, externalPosition: clock), () =>
            {
                converter = new(metrics);
                return converter;
            }, async (action, token) =>
            {
                var started = Stopwatch.GetTimestamp();
                await Task.Delay(TimeSpan.FromMilliseconds(8), token);
                metrics.UiQueueMilliseconds.Enqueue(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
                action();
            }, update =>
            {
                if (update.Frame is { } frame)
                {
                    metrics.Presentations.Enqueue((Stopwatch.GetTimestamp(), update.Snapshot));
                    metrics.PresentedDimensions.Enqueue((frame.Width, frame.Height));
                    firstFrame.TrySetResult();
                }
                else if (update.Snapshot.Error is { } error)
                {
                    firstFrame.TrySetException(error);
                }
            }, audioFactory: null);
        try
        {
            await controller.SwitchDecodeModeAsync(mode);
            await controller.OpenAsync(path);
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await controller.PlayAsync();
            metrics.BeginPlayback();
            await Task.Delay(TimeSpan.FromSeconds(2));
            metrics.EndPlayback();
            endingSnapshot = controller.Snapshot;
            var playing = metrics.GetPlayingPresentations();
            Assert.Null(endingSnapshot.Error);
            Assert.False(endingSnapshot.AudioAvailable);
            Assert.Equal(VideoPlaybackState.PLAYING, endingSnapshot.State);
            Assert.True(endingSnapshot.Position >= new MediaTime(2));
            Assert.True(endingSnapshot.Position < new MediaTime(5));
            Assert.True(playing.Length >= 15, $"{mode} delivered only {playing.Length} frames in two seconds.");
            Assert.True(playing.Count(sample => sample.Timestamp <= metrics.PlaybackMidpoint) >= 5);
            Assert.True(playing.Count(sample => sample.Timestamp > metrics.PlaybackMidpoint) >= 5);
            Assert.True(playing[^1].Snapshot.PresentedFrameTime >= endingSnapshot.Position - new MediaTime(1, 4));
            Assert.All(playing, sample =>
            {
                var snapshot = sample.Snapshot;
                Assert.True(snapshot.PresentedFrameTime <= snapshot.PresentedAtPosition);
                Assert.True(snapshot.PresentedAtPosition < snapshot.PresentedFrameEnd);
                Assert.Equal(new MediaTime(1, 60), snapshot.PresentedFrameEnd!.Value - snapshot.PresentedFrameTime!.Value);
                Assert.InRange(snapshot.PreparedFrameCount, 0, 2);
                Assert.InRange(snapshot.PreparationPendingCount, 0, 2);
            });
            for (var index = 1; index < playing.Length; index++)
            {
                Assert.True(playing[index].Snapshot.PresentedFrameTime > playing[index - 1].Snapshot.PresentedFrameTime);
            }
            Assert.All(metrics.PresentedDimensions, size => Assert.Equal((1920, 1080), size));
            var actual = Assert.IsType<VideoDecodeSessionInfo>(source!.SessionInfo);
            Assert.Equal(mode, actual.RequestedMode);
            if (mode == VideoDecodeMode.Software)
            {
                Assert.Equal(VideoDecoderBackend.Software, actual.ActiveBackend);
                Assert.False(actual.HardwareConfirmed);
            }
            else if (mode == VideoDecodeMode.Hardware || OperatingSystem.IsMacOS())
            {
                Assert.True(actual.HardwareConfirmed, actual.FallbackReason);
                Assert.NotEqual(VideoDecoderBackend.Software, actual.ActiveBackend);
                Assert.Empty(actual.FallbackReason);
                Assert.True(actual.DownloadNanoseconds > 0);
                if (OperatingSystem.IsMacOS())
                {
                    Assert.Equal(VideoDecoderBackend.VideoToolbox, actual.ActiveBackend);
                }
            }
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            metrics.EndPlayback();
            endingSnapshot ??= controller.Snapshot;
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var finalResources = (Decoders: FfmpegVideoDecoder.GetLiveDecoderCount(),
                Frames: FfmpegVideoDecoder.GetLiveFrameCount(), Converters: SdrVideoConverter.LiveConverterCount);
            await metrics.WriteReportAsync(mode, source?.SessionInfo, endingSnapshot, failure, initialResources, finalResources);
            Assert.Equal(initialResources, finalResources);
            if (source is not null)
            {
                Assert.Equal(1, source.DisposeCount);
            }
            if (converter is not null)
            {
                Assert.Equal(1, converter.DisposeCount);
            }
        }
    }
}
