using System.Runtime.InteropServices;
using System.Text.Json;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Media.Decoding;
using AegiNext.Media.Encoding;

namespace AegiNext.Media.Tests.Encoding;

public sealed class VideoEncodingOptionsTests
{
    [Fact]
    public void DefaultsKeepSoftwareCrfAndNativeAbiIsExplicitlyVersioned()
    {
        var request = CreateRequest();
        Assert.Equal(VideoEncodingMode.SOFTWARE, request.EncodingMode);
        Assert.Equal(VideoDecodeMode.Auto, request.DecodeMode);
        Assert.Equal(20, request.Crf);
        Assert.Equal(8000000, request.VideoBitrate);
        Assert.Equal(80, Marshal.SizeOf<NativeExportRequest>());
        Assert.Equal(64, Marshal.OffsetOf<NativeExportRequest>(nameof(NativeExportRequest.EncodingMode)).ToInt32());
        VideoExporter.Validate(request);
    }

    [Theory]
    [InlineData(99999)]
    [InlineData(200000001)]
    public void HardwareBitrateIsValidatedBeforeStartingWorker(int bitrate)
    {
        var request = CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, VideoBitrate = bitrate };
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(request));
    }

    [Fact]
    public void InactiveQualityFieldsDoNotBlockTheSelectedEncodingMode()
    {
        VideoExporter.Validate(CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, Crf = -1 });
        VideoExporter.Validate(CreateRequest() with { VideoBitrate = -1 });
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(CreateRequest() with { EncodingMode = (VideoEncodingMode)9 }));
        Assert.Throws<ArgumentException>(() => VideoExporter.Validate(CreateRequest() with { DecodeMode = (VideoDecodeMode)9 }));
    }

    [Fact]
    public void WorkerWireCarriesHardwareModeBitrateAndActualEncoderIdentity()
    {
        var job = new ExportWorkerJob(JsonSerializer.SerializeToElement(new ProjectDocument(), ExportWire.Options),
            Path.GetTempPath(), Path.GetTempPath(), ".mp4", VideoCodec.Hevc, "slow", 20, AudioExportMode.None,
            192000, "ffmpeg", VideoEncodingMode.HARDWARE, 12000000, VideoDecodeMode.Software);
        var serialized = JsonSerializer.Serialize(job, ExportWire.Options);
        var restored = Assert.IsType<ExportWorkerJob>(JsonSerializer.Deserialize<ExportWorkerJob>(serialized, ExportWire.Options));
        Assert.Equal(VideoEncodingMode.HARDWARE, restored.EncodingMode);
        Assert.Equal(12000000, restored.VideoBitrate);
        Assert.Equal(VideoDecodeMode.Software, restored.DecodeMode);
        var message = new ExportWorkerMessage("complete", 42, Encoder: "hevc_videotoolbox",
            Decoder: new(VideoDecodeMode.Software, VideoDecoderBackend.Software, false, string.Empty, 0, 42),
            OutputColor: new(1, 1, 1, 1, 1, 0, 0));
        Assert.Equal(message, JsonSerializer.Deserialize<ExportWorkerMessage>(JsonSerializer.Serialize(message, ExportWire.Options), ExportWire.Options));
    }

    [Theory]
    [InlineData(VideoDecodeMode.Auto)]
    [InlineData(VideoDecodeMode.Software)]
    [InlineData(VideoDecodeMode.Hardware)]
    public void LegacyWorkerWithoutDecodeAndEffectiveColorDiagnosticsCannotCommit(VideoDecodeMode mode)
    {
        var request = CreateRequest() with { DecodeMode = mode };
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedDecoder(request,
            new("complete", 42, Encoder: "libx264")));
    }

    [Fact]
    public void RequiredHardwareDecodeCannotAcceptAWorkerSoftwareFallback()
    {
        var request = CreateRequest() with { DecodeMode = VideoDecodeMode.Hardware };
        var message = new ExportWorkerMessage("complete", 42, Encoder: "libx264",
            Decoder: new(VideoDecodeMode.Hardware, VideoDecoderBackend.Software, false, "unavailable", 1, 42),
            OutputColor: new(1, 1, 1, 1, 1, 0, 0));
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedDecoder(request, message));
        VideoExporter.ValidateCompletedDecoder(request, message with
        {
            Decoder = new(VideoDecodeMode.Hardware, VideoDecoderBackend.VideoToolbox, true, string.Empty, 1, 42)
        });
    }

    [Theory]
    [InlineData(VideoDecoderBackend.Software, true, 1UL)]
    [InlineData(VideoDecoderBackend.VideoToolbox, false, 1UL)]
    [InlineData(VideoDecoderBackend.Software, false, 0UL)]
    public void AutomaticWorkerMustReportAConsistentActualDecoder(VideoDecoderBackend backend, bool confirmed, ulong generation)
    {
        var message = new ExportWorkerMessage("complete", 42, Encoder: "libx264",
            Decoder: new(VideoDecodeMode.Auto, backend, confirmed, string.Empty, generation, 42),
            OutputColor: new(1, 1, 1, 1, 1, 0, 0));
        Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedDecoder(CreateRequest(), message));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("libx264")]
    [InlineData("libx265")]
    [InlineData("hevc_videotoolbox")]
    [InlineData("h264_unknown")]
    public void HardwareWorkerMustConfirmMatchingBackendBeforeAtomicOutputCommit(string? encoder)
    {
        var request = CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, Codec = VideoCodec.H264 };
        var error = Assert.Throws<InvalidDataException>(() => VideoExporter.ValidateCompletedEncoder(request, encoder));
        Assert.Contains("worker", error.Message, StringComparison.Ordinal);
        Assert.Contains("未提交成片", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(VideoCodec.Auto, "h264_videotoolbox")]
    [InlineData(VideoCodec.H264, "h264_nvenc")]
    [InlineData(VideoCodec.H264, "h264_qsv")]
    [InlineData(VideoCodec.H264, "h264_amf")]
    [InlineData(VideoCodec.Hevc, "hevc_videotoolbox")]
    [InlineData(VideoCodec.Hevc, "hevc_nvenc")]
    [InlineData(VideoCodec.Hevc, "hevc_qsv")]
    [InlineData(VideoCodec.Hevc, "hevc_amf")]
    public void MatchingHardwareIdentityIsAcceptedAndLegacySoftwareWorkerRemainsUsable(VideoCodec codec, string encoder)
    {
        var request = CreateRequest() with { EncodingMode = VideoEncodingMode.HARDWARE, Codec = codec };
        VideoExporter.ValidateCompletedEncoder(request, encoder);
        VideoExporter.ValidateCompletedEncoder(request with { EncodingMode = VideoEncodingMode.SOFTWARE }, null);
    }

    private static VideoExportRequest CreateRequest()
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty,
            ExternalPath: Path.Combine(Path.GetTempPath(), "encoding-options.mp4"));
        var document = new ProjectDocument
        {
            Width = 96, Height = 64, Assets = [asset], Media = new(asset.Id, 0, null, MediaTime.Zero)
        };
        return new(document, Path.GetTempPath(), Path.Combine(Path.GetTempPath(), "encoded-options.mp4"));
    }
}
