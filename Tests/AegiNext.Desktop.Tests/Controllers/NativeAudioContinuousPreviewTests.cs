using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Audio;
using AegiNext.Media.Decoding;
using AegiNext.Media.Playback;
using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Tests.Controllers;

[Collection("Native preview timing")]
public sealed class NativeAudioContinuousPreviewTests
{
    private static readonly JsonSerializerOptions jsonOptions = new() { WriteIndented = true };

    /// <summary>用户定位和画质失效后，真实音频主时钟与原生视频持续呈现。</summary>
    [SystemAudioPreviewFact]
    public async Task SystemAudioClockKeepsPresentingAfterPlayingSeeksAndPreviewInvalidation()
    {
        using var fixture = await NativeContinuousPreviewFixture.CreateAsync(true);
        var initialResources = (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
        var presentations = new ConcurrentQueue<VideoPreviewSnapshot>();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observations = new List<object>();
        var mode = OperatingSystem.IsMacOS() ? VideoDecodeMode.Hardware : VideoDecodeMode.Software;
        string? failure = null;
        await using var controller = new VideoPreviewController(VideoPreviewProbe.ProbeAsync,
            (path, index, clock, options) => new(token => VideoFrameNavigator.Open(path, index, options, token),
                externalPosition: clock), () => new SdrVideoConverter(), async (action, token) =>
            {
                await Task.Delay(8, token);
                action();
            }, update =>
            {
                if (update.Frame is not null)
                {
                    presentations.Enqueue(update.Snapshot);
                    firstFrame.TrySetResult();
                }
            }, AudioPlaybackSession.OpenAsync);
        try
        {
            await controller.SwitchDecodeModeAsync(mode);
            await controller.OpenAsync(fixture.MediaPath);
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
            controller.SetVolume(0.35F);
            await controller.PlayAsync();
            for (var cycle = 0; cycle < 4; cycle++)
            {
                controller.SetMuted(cycle % 2 == 0);
                await controller.SeekForPlaybackAsync(new(cycle + 1, 4), true);
                controller.InvalidatePreview();
                await controller.RefreshPausedPreviewAsync();
                await ObservePlaybackAsync(controller, presentations, observations, TimeSpan.FromMilliseconds(400));
                Assert.Equal(0.35F, controller.Snapshot.Volume);
                Assert.Equal(cycle % 2 == 0, controller.Snapshot.IsMuted);
            }
            await ObservePlaybackAsync(controller, presentations, observations, TimeSpan.FromSeconds(2));
            await controller.PauseAsync();
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            var clock = controller.AudioClock;
            var ending = controller.Snapshot;
            var pipeline = controller.PipelineDiagnostics;
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15));
            await File.WriteAllTextAsync(GetValidationReportPath("transport"), JsonSerializer.Serialize<object>(new
            {
                RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                Mode = mode.ToString(),
                Fixture = "1920x1080 H.264 60 fps, explicit BT709, silent AAC 48000 Hz stereo",
                PlayingSeekCount = 4,
                PreviewInvalidationCount = 4,
                Observations = observations,
                FinalClock = clock,
                FinalPosition = ending.Position.ToString(),
                AudioError = ending.AudioError?.ToString(),
                Pipeline = pipeline,
                Failure = failure
            }, jsonOptions));
            var finalResources = (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
            Assert.Equal(initialResources, finalResources);
        }
    }

    /// <summary>真实系统音频主时钟在连续视频、暂停和定位之后仍可用，并在关闭时释放原生视频资源。</summary>
    [SystemAudioPreviewFact]
    public async Task SystemAudioClockKeepsVideoAdvancingAcrossRepeatedPauseSeekAndResume()
    {
        var reportPath = GetValidationReportPath();
        using var fixture = await NativeContinuousPreviewFixture.CreateAsync(true);
        var initialResources = (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
        var metrics = new NativePreviewTimingMetrics();
        var firstFrame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var presentations = new ConcurrentQueue<VideoPreviewSnapshot>();
        var observations = new List<object>();
        var mode = OperatingSystem.IsMacOS() ? VideoDecodeMode.Hardware : VideoDecodeMode.Software;
        string? failure = null;
        await using var controller = new VideoPreviewController(VideoPreviewProbe.ProbeAsync,
            (path, index, clock, options) => new(token => new MeasuredNativePreviewSource(
                VideoFrameNavigator.Open(path, index, options, token), metrics), TimeProvider.System, externalPosition: clock),
            () => new MeasuredNativePreviewConverter(metrics), async (action, token) =>
            {
                await Task.Delay(8, token);
                action();
            }, update =>
            {
                if (update.Frame is not null)
                {
                    presentations.Enqueue(update.Snapshot);
                    firstFrame.TrySetResult();
                }
                else if (update.Snapshot.Error is { } error)
                {
                    firstFrame.TrySetException(error);
                }
            }, AudioPlaybackSession.OpenAsync);
        try
        {
            await controller.SwitchDecodeModeAsync(mode);
            await controller.OpenAsync(fixture.MediaPath);
            await firstFrame.Task.WaitAsync(TimeSpan.FromSeconds(15));
            Assert.True(controller.Snapshot.AudioAvailable);
            Assert.Null(controller.Snapshot.AudioError);
            await controller.PlayAsync();
            for (var cycle = 0; cycle < 6; cycle++)
            {
                await ObservePlaybackAsync(controller, presentations, observations, TimeSpan.FromMilliseconds(400));
                await controller.PauseAsync();
                var frozen = controller.Snapshot.Position;
                await Task.Delay(50);
                Assert.Equal(frozen, controller.Snapshot.Position);
                await controller.SeekAsync(new MediaTime(cycle + 1, 10));
                Assert.Equal(new MediaTime(cycle + 1, 10), controller.Snapshot.Position);
                await controller.PlayAsync();
            }
            await ObservePlaybackAsync(controller, presentations, observations, TimeSpan.FromSeconds(4));
            await controller.PauseAsync();
            Assert.Null(controller.Snapshot.AudioError);
        }
        catch (Exception error)
        {
            failure = error.ToString();
            throw;
        }
        finally
        {
            var clock = controller.AudioClock;
            var ending = controller.Snapshot;
            var pipeline = controller.PipelineDiagnostics;
            await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(15));
            var finalResources = (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize<object>(new
            {
                RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
                Mode = mode.ToString(),
                Fixture = "1920x1080 H.264 60 fps, explicit BT709, silent AAC 48000 Hz stereo",
                SimulatedUiQueueMilliseconds = 8,
                Observations = observations,
                FinalClock = clock,
                FinalPosition = ending.Position.ToString(),
                AudioError = ending.AudioError?.ToString(),
                Pipeline = pipeline,
                Failure = failure
            }, jsonOptions));
            Assert.Equal(initialResources, finalResources);
        }
    }

    private static async Task ObservePlaybackAsync(VideoPreviewController controller,
        ConcurrentQueue<VideoPreviewSnapshot> presentations, List<object> observations, TimeSpan duration)
    {
        var start = controller.Snapshot.Position;
        var presented = presentations.Count;
        await Task.Delay(duration);
        var snapshot = controller.Snapshot;
        var clock = Assert.IsType<AudioOutputClockSnapshot>(controller.AudioClock);
        observations.Add(new
        {
            DurationSeconds = duration.TotalSeconds,
            Start = start.ToString(),
            End = snapshot.Position.ToString(),
            NewPresentations = presentations.Count - presented,
            Clock = clock,
            AudioError = snapshot.AudioError?.ToString(),
            Pipeline = controller.PipelineDiagnostics
        });
        Assert.Null(snapshot.AudioError);
        Assert.Null(snapshot.Error);
        Assert.Equal(AudioClockQuality.SYSTEM, clock.Quality);
        Assert.Equal(OperatingSystem.IsMacOS() ? "CoreAudio" : "WASAPI", clock.Backend);
        Assert.Equal(VideoPlaybackState.PLAYING, snapshot.State);
        Assert.True(snapshot.Position >= start + MediaTime.FromTimeSpan(duration / 2),
            $"System clock stalled at {snapshot.Position}, starting from {start}, after {duration}.");
        var playing = presentations.ToArray().Skip(presented).Where(value => value.State == VideoPlaybackState.PLAYING).ToArray();
        Assert.True(playing.Length >= Math.Max(3, (int)(duration.TotalSeconds * 10)),
            $"Only {playing.Length} valid video frames arrived in {duration} with a real system audio clock. {controller.PipelineDiagnostics}");
        Assert.All(playing, value =>
        {
            Assert.True(value.PresentedFrameTime <= value.PresentedAtPosition);
            Assert.True(value.PresentedAtPosition < value.PresentedFrameEnd);
        });
        Assert.True(playing[^1].PresentedFrameEnd > snapshot.Position - new MediaTime(1, 4));
    }

    private static string GetValidationReportPath(string? operation = null)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "AegiNext.sln")))
        {
            root = root.Parent ?? throw new DirectoryNotFoundException("Cannot locate validation artifact directory.");
        }
        var path = Path.Combine(root.FullName, "artifacts", "validation", "timing",
            $"preview-system-audio-{(operation is null ? string.Empty : operation + "-")}{RuntimeInformation.RuntimeIdentifier}.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }
}
