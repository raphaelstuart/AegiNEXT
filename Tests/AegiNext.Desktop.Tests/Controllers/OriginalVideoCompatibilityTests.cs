using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Channels;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;
using AegiNext.Media.Decoding;
using AegiNext.Media.Preview;
using AegiNext.Media.Probing;

namespace AegiNext.Desktop.Tests.Controllers;

/// <summary>使用显式指定的 H.264、HEVC 444 十位或 ProRes 4444 原片验证预览、定位与模式切换。</summary>
[Collection("Native preview timing")]
public sealed class OriginalVideoCompatibilityTests
{
    private static readonly JsonSerializerOptions reportOptions = new() { WriteIndented = true, IncludeFields = true };
    private static readonly string[] supportedCodecs = ["h264", "hevc", "prores"];
    private static readonly string[] proResFormats = ["yuv444p12le", "yuva444p12le"];

    /// <summary>Auto 与 CPU 通过真实控制器呈现相同像素，并验证设备支持时的严格 GPU 模式切换。</summary>
    [OriginalVideoCompatibilityFact]
    public async Task OriginalPresentsMatchingCpuAndAutoFramesAndChecksStrictGpuMode()
    {
        var path = Environment.GetEnvironmentVariable("AEGINEXT_COMPATIBILITY_MEDIA_PATH")!;
        Assert.True(Path.IsPathFullyQualified(path));
        Assert.True(File.Exists(path));
        var source = new FileInfo(path);
        var initialSource = (source.Length, source.LastWriteTimeUtc);
        var probe = new FfprobeMediaProbe(new(MediaToolchain.ResolveFfprobe()));
        var report = await probe.ProbeAsync(path);
        var video = Assert.Single(report.Asset.Streams.Where(stream => stream.Video is not null));
        Assert.Contains(video.CodecName, supportedCodecs);
        var proRes = video.CodecName == "prores";
        if (proRes)
        {
            Assert.Contains(video.Video!.PixelFormat, proResFormats);
        }
        else if (video.CodecName == "hevc")
        {
            Assert.Equal("yuv444p10le", video.Video!.PixelFormat);
        }
        else
        {
            Assert.Equal("yuv420p", video.Video!.PixelFormat);
        }
        Assert.True(video.Video.Width > 0);
        Assert.True(video.Video.Height > 0);
        var resources = Resources();
        var expectedHashes = new Dictionary<int, string>();
        var expectedPixels = new Dictionary<int, byte[]>();
        var positions = video.CodecName == "hevc" ? new[] { 0, 30, 60, 900, 1430, 60, 30 } : new[] { 0, 1, 2, 4, 5, 2, 1 };
        var restoredPosition = positions[^1];
        var measurements = new List<object>();
        try
        {
            foreach (var mode in new[] { VideoDecodeMode.Software, VideoDecodeMode.Auto })
            {
                var updates = Channel.CreateUnbounded<VideoPreviewUpdate>(new() { SingleReader = true });
                await using var controller = new VideoPreviewController(VideoPreviewProbe.ProbeAsync,
                    (filePath, index, clock, options) => new(token => VideoFrameNavigator.Open(filePath, index, options, token),
                        externalPosition: clock),
                    () => new SdrVideoConverter(new(960, 540)), Dispatch, update => updates.Writer.TryWrite(update));
                await controller.SwitchDecodeModeAsync(mode);
                await controller.OpenAsync(path).WaitAsync(TimeSpan.FromSeconds(30));
                foreach (var seconds in positions)
                {
                    var target = new MediaTime(seconds);
                    if (seconds != 0)
                    {
                        while (updates.Reader.TryRead(out _))
                        {
                        }
                        await controller.SeekAsync(target).WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    var presented = await ReadCurrentPresentationAsync(controller, updates.Reader, target);
                    var frame = presented.Frame!;
                    Assert.Equal(960, frame.Width);
                    Assert.Equal(540, frame.Height);
                    Assert.True(presented.Snapshot.IsPresentedFrameCurrent);
                    Assert.Equal(target, presented.Snapshot.Position);
                    Assert.True(presented.Snapshot.PresentedFrameTime <= target);
                    Assert.True(target < presented.Snapshot.PresentedFrameEnd);
                    var session = Assert.IsType<VideoDecodeSessionInfo>(presented.Snapshot.DecodeSessionInfo);
                    Assert.Equal(mode, session.RequestedMode);
                    if (mode == VideoDecodeMode.Software)
                    {
                        Assert.Equal(VideoDecoderBackend.Software, session.ActiveBackend);
                        Assert.False(session.HardwareConfirmed);
                    }
                    else if (session.ActiveBackend == VideoDecoderBackend.Software)
                    {
                        Assert.False(session.HardwareConfirmed);
                        Assert.NotEmpty(session.FallbackReason);
                    }
                    else
                    {
                        Assert.True(session.HardwareConfirmed);
                        Assert.Empty(session.FallbackReason);
                    }
                    if (mode == VideoDecodeMode.Auto &&
                        (Environment.GetEnvironmentVariable("AEGINEXT_REQUIRED_HARDWARE_CODECS") ?? "").Split(',').Contains(video.CodecName))
                    {
                        Assert.True(session.HardwareConfirmed, session.FallbackReason);
                    }
                    var hash = Convert.ToHexString(SHA256.HashData(frame.Pixels.Span));
                    if (expectedHashes.TryGetValue(seconds, out var expected))
                    {
                        if (proRes && session.HardwareConfirmed)
                        {
                            AssertProResPreview(expectedPixels[seconds], frame.Pixels.Span);
                            if (OperatingSystem.IsWindows())
                            {
                                Assert.Equal(VideoDecoderBackend.Vulkan, session.ActiveBackend);
                            }
                        }
                        else
                        {
                            Assert.Equal(expected, hash);
                        }
                    }
                    else
                    {
                        expectedHashes.Add(seconds, hash);
                        expectedPixels.Add(seconds, frame.Pixels.ToArray());
                    }
                    measurements.Add(new
                    {
                        mode = mode.ToString(), seconds, hash,
                        frameTime = presented.Snapshot.PresentedFrameTime!.Value.ToString(),
                        nextFrameTime = presented.Snapshot.PresentedFrameEnd!.Value.ToString(),
                        session.ActiveBackend, session.HardwareConfirmed, session.FallbackReason
                    });
                }

                if (mode == VideoDecodeMode.Auto)
                {
                    var hardwareSupported = controller.Snapshot.DecodeSessionInfo!.HardwareConfirmed;
                    while (updates.Reader.TryRead(out _))
                    {
                    }
                    if (hardwareSupported)
                    {
                        await controller.SwitchDecodeModeAsync(VideoDecodeMode.Hardware).WaitAsync(TimeSpan.FromSeconds(30));
                        var strict = await ReadCurrentPresentationAsync(controller, updates.Reader, new(restoredPosition));
                        Assert.Equal(VideoDecodeMode.Hardware, controller.DecodeMode);
                        Assert.True(strict.Snapshot.DecodeSessionInfo!.HardwareConfirmed);
                        Assert.Empty(strict.Snapshot.DecodeSessionInfo.FallbackReason);
                        if (proRes)
                        {
                            AssertProResPreview(expectedPixels[restoredPosition], strict.Frame!.Pixels.Span);
                        }
                        else
                        {
                            Assert.Equal(expectedHashes[restoredPosition], Convert.ToHexString(SHA256.HashData(strict.Frame!.Pixels.Span)));
                        }
                        await controller.SwitchDecodeModeAsync(VideoDecodeMode.Auto).WaitAsync(TimeSpan.FromSeconds(30));
                    }
                    else
                    {
                        var failure = await Record.ExceptionAsync(
                            () => controller.SwitchDecodeModeAsync(VideoDecodeMode.Hardware).WaitAsync(TimeSpan.FromSeconds(30)));
                        Assert.True(failure is NotSupportedException or InvalidDataException, failure?.ToString());
                    }
                    Assert.Null(controller.Snapshot.Error);
                    var restored = await ReadCurrentPresentationAsync(controller, updates.Reader, new(restoredPosition));
                    Assert.True(restored.Snapshot.IsPresentedFrameCurrent);
                    if (proRes && hardwareSupported)
                    {
                        AssertProResPreview(expectedPixels[restoredPosition], restored.Frame!.Pixels.Span);
                    }
                    else
                    {
                        Assert.Equal(expectedHashes[restoredPosition], Convert.ToHexString(SHA256.HashData(restored.Frame!.Pixels.Span)));
                    }
                    Assert.Equal(VideoDecodeMode.Auto, controller.DecodeMode);
                    measurements.Add(new { strictGpuSupported = hardwareSupported, restoredMode = controller.DecodeMode.ToString() });
                }
                await controller.CloseAsync().WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal(resources, Resources());
            }
        }
        finally
        {
            Assert.Equal(resources, Resources());
            source.Refresh();
            Assert.Equal(initialSource, (source.Length, source.LastWriteTimeUtc));
        }

        if (Environment.GetEnvironmentVariable("AEGINEXT_COMPATIBILITY_REPORT_PATH") is { Length: > 0 } reportPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new { source = path, measurements, resources }, reportOptions));
        }
    }

    private static async Task<VideoPreviewUpdate> ReadCurrentPresentationAsync(VideoPreviewController controller,
        ChannelReader<VideoPreviewUpdate> updates, MediaTime target)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (await updates.WaitToReadAsync(timeout.Token))
        {
            while (updates.TryRead(out var update))
            {
                var current = controller.Snapshot;
                if (update.Snapshot.Epoch != current.Epoch)
                {
                    continue;
                }
                Assert.Null(update.Snapshot.Error);
                if (update.Frame is not null && update.Snapshot.Position == target &&
                    update.Snapshot.IsPresentedFrameCurrent)
                {
                    return update;
                }
            }
        }
        throw new InvalidOperationException("预览通道未交付当前定位的有效帧。");
    }

    private static (uint Decoders, uint Frames, uint Converters) Resources()
    {
        return (FfmpegVideoDecoder.GetLiveDecoderCount(), FfmpegVideoDecoder.GetLiveFrameCount(), SdrVideoConverter.LiveConverterCount);
    }

    private static void AssertProResPreview(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        Assert.Equal(expected.Length, actual.Length);
        var maximum = 0;
        var total = 0L;
        for (var sample = 0; sample < expected.Length; sample++)
        {
            var error = Math.Abs(expected[sample] - actual[sample]);
            maximum = Math.Max(maximum, error);
            total += error;
            if (sample % 4 == 3)
            {
                Assert.Equal(byte.MaxValue, actual[sample]);
            }
        }
        Assert.True(maximum <= 3, $"ProRes SDR preview maximum byte error {maximum}.");
        Assert.True(total / (double)expected.Length <= 0.3, $"ProRes SDR preview mean byte error {total / (double)expected.Length}.");
    }

    private static Task Dispatch(Action action, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return Task.CompletedTask;
    }
}
