using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Media.Probing;

namespace AegiNext.Media.Tests.Probing;

public class FfprobeJsonReaderTests
{
    [Fact]
    public void ReadsAllStreamsAndKeepsReportedTimesSeparateFromPts()
    {
        var asset = FfprobeJsonReader.Read("""
            {
              "streams": [
                {
                  "index": 3, "codec_type": "video", "codec_name": "hevc",
                  "time_base": "1/90000", "start_pts": -9000,
                  "duration_ts": "180180", "start_time": "-0.099999",
                  "duration": "2.002001", "width": 3840, "height": 2160,
                  "pix_fmt": "yuv420p10le", "bits_per_raw_sample": "10",
                  "sample_aspect_ratio": "1:1", "r_frame_rate": "30000/1001",
                  "avg_frame_rate": "24000/1001", "color_range": "tv",
                  "color_space": "bt2020nc", "color_transfer": "smpte2084",
                  "color_primaries": "bt2020", "chroma_location": "left",
                  "tags": { "language": "jpn", "title": "本編" },
                  "disposition": { "default": 1, "attached_pic": 0 }
                },
                {
                  "index": 7, "codec_type": "audio", "codec_name": "aac",
                  "time_base": "1/48000", "start_pts": "-1024",
                  "duration_ts": 96000, "sample_rate": "48000", "channels": 6,
                  "channel_layout": "5.1", "tags": { "language": "eng" }
                },
                { "index": 9, "codec_type": "subtitle", "codec_name": "subrip" }
              ],
              "format": {
                "format_name": "matroska,webm", "start_time": "-0.1",
                "duration": "2.1", "tags": { "title": "例" }
              }
            }
            """);

        Assert.Equal(3, asset.Streams.Length);
        Assert.Equal("matroska,webm", asset.FormatName);
        Assert.Equal(new MediaTime(-1, 10), asset.ReportedStart);
        Assert.Equal(new MediaTime(21, 10), asset.ReportedDuration);
        Assert.Equal("例", asset.Tags["title"]);
        var stream = asset.Streams[0];
        Assert.Equal(3, stream.Index);
        Assert.Equal("hevc", stream.CodecName);
        Assert.Equal(-9000, stream.Timing.StartPts);
        Assert.Equal(new MediaTimeBase(1, 90000), stream.Timing.TimeBase);
        Assert.Equal(new MediaTimestamp(-9000, new(1, 90000)), stream.Timing.StartTimestamp);
        Assert.Equal(180180, stream.Timing.DurationTicks);
        Assert.Equal(new MediaTime(-99999, 1000000), stream.Timing.ReportedStart);
        Assert.Equal(new MediaTime(2002001, 1000000), stream.Timing.ReportedDuration);
        Assert.Equal("jpn", stream.Tags["language"]);
        Assert.Equal(1, stream.Disposition["default"]);
        var video = Assert.IsType<MediaVideoInfo>(stream.Video);
        Assert.Equal(3840, video.Width);
        Assert.Equal(2160, video.Height);
        Assert.Equal("yuv420p10le", video.PixelFormat);
        Assert.Equal(10, video.BitsPerRawSample);
        Assert.Equal(new MediaRatio(1, 1), video.SampleAspectRatio);
        Assert.Equal(new MediaRatio(30000, 1001), video.FrameRate);
        Assert.Equal(new MediaRatio(24000, 1001), video.AverageFrameRate);
        Assert.True(video.Color.IsPq);
        Assert.False(video.Color.IsHlg);
        Assert.Equal("bt2020", video.Color.Primaries);
        Assert.Equal("bt2020nc", video.Color.Matrix);
        Assert.Equal("tv", video.Color.Range);
        Assert.Equal("left", video.Color.ChromaLocation);
        var audio = Assert.IsType<MediaAudioInfo>(asset.Streams[1].Audio);
        Assert.Equal(48000, audio.SampleRate);
        Assert.Equal(6, audio.Channels);
        Assert.Equal("5.1", audio.ChannelLayout);
        Assert.Null(asset.Streams[2].Video);
        Assert.Null(asset.Streams[2].Audio);
    }

    [Fact]
    public void PreservesStreamHdrSideDataAndDisplayMatrixWithoutClaimingFrameCoverage()
    {
        var asset = FfprobeJsonReader.Read("""
            { "streams": [{ "index": 0, "codec_type": "video", "side_data_list": [
              { "side_data_type": "Mastering display metadata",
                "red_x": "34000/50000", "red_y": "16000/50000",
                "green_x": "13250/50000", "green_y": "34500/50000",
                "blue_x": "7500/50000", "blue_y": "3000/50000",
                "white_point_x": "15635/50000", "white_point_y": "16450/50000",
                "min_luminance": "1/10000", "max_luminance": "10000000/10000" },
              { "side_data_type": "Content light level metadata", "max_content": 1000, "max_average": "400" },
              { "side_data_type": "Display Matrix", "displaymatrix": "\n00000000: 0 -65536 0\n00000001: 65536 0 0\n00000002: 0 0 1073741824\n", "rotation": -90.5 },
              { "side_data_type": "DOVI configuration record", "dv_profile": 8 }
            ] }] }
            """);

        var video = Assert.IsType<MediaVideoInfo>(Assert.Single(asset.Streams).Video);
        var mastering = Assert.Single(video.MasteringDisplays);
        Assert.Equal(new MediaRatio(17, 25), mastering.RedX);
        Assert.Equal(new MediaRatio(1, 10000), mastering.MinLuminance);
        Assert.Equal(new MediaRatio(1000, 1), mastering.MaxLuminance);
        var light = Assert.Single(video.ContentLightLevels);
        Assert.Equal(1000u, light.MaxContentLightLevel);
        Assert.Equal(400u, light.MaxFrameAverageLightLevel);
        var matrix = Assert.Single(video.DisplayMatrices);
        Assert.Equal(new MediaRatio(-181, 2), matrix.RotationDegrees);
        Assert.Contains("1073741824", matrix.MatrixText);
        Assert.Contains("DOVI configuration record", video.SideDataTypes);
    }

    [Fact]
    public void UnknownSentinelsStayUnknownAndRawColorNamesAreRetained()
    {
        var asset = FfprobeJsonReader.Read("""
            { "streams": [{ "index": 0, "codec_type": "video",
              "time_base": "0/0", "start_pts": "-9223372036854775808",
              "duration_ts": "N/A", "start_time": "N/A", "duration": null,
              "width": 0, "height": 0, "bits_per_raw_sample": "0",
              "sample_aspect_ratio": "0:1", "r_frame_rate": "0/0", "avg_frame_rate": "0/1",
              "color_transfer": "future-transfer", "color_primaries": "unknown",
              "color_space": "reserved-new-value" }] }
            """);

        var stream = Assert.Single(asset.Streams);
        Assert.Null(stream.Timing.TimeBase);
        Assert.Null(stream.Timing.StartPts);
        Assert.Null(stream.Timing.StartTimestamp);
        Assert.Null(stream.Timing.DurationTicks);
        Assert.Null(stream.Timing.ReportedStart);
        var video = Assert.IsType<MediaVideoInfo>(stream.Video);
        Assert.Null(video.Width);
        Assert.Null(video.BitsPerRawSample);
        Assert.Null(video.SampleAspectRatio);
        Assert.Null(video.FrameRate);
        Assert.Null(video.AverageFrameRate);
        Assert.Equal("future-transfer", video.Color.Transfer);
        Assert.Equal("unknown", video.Color.Primaries);
        Assert.Equal("reserved-new-value", video.Color.Matrix);
        Assert.False(video.Color.IsPq);
        Assert.False(video.Color.IsHlg);
    }

    [Fact]
    public void KnownPtsWithoutTimeBaseIsPreservedWithoutInventingTimestamp()
    {
        var stream = Assert.Single(FfprobeJsonReader.Read("""
            { "streams": [{ "index": 0, "start_pts": 123 }] }
            """).Streams);

        Assert.Equal(123, stream.Timing.StartPts);
        Assert.Null(stream.Timing.StartTimestamp);
    }

    [Fact]
    public void ParsesExactReportedSecondsByReducingBeforeCheckingLongRange()
    {
        var asset = FfprobeJsonReader.Read("""
            { "format": { "start_time": "9223372036854775807.0000000000000000000", "duration": "1.25e-3" } }
            """);

        Assert.Equal(new MediaTime(long.MaxValue), asset.ReportedStart);
        Assert.Equal(new MediaTime(1, 800), asset.ReportedDuration);
        Assert.Empty(asset.Streams);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"streams\":{}}")]
    [InlineData("{\"streams\":[{}]}")]
    [InlineData("{\"streams\":[{\"index\":-1}]}")]
    [InlineData("{\"streams\":[{\"index\":0},{\"index\":0}]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"time_base\":\"1/0\"}]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"start_pts\":\"9223372036854775808\"}]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"duration_ts\":-1}]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"time_base\":\"NaN\"}]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"codec_type\":\"video\",\"width\":-20}]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"codec_type\":\"audio\",\"sample_rate\":\"48000x\"}]}")]
    [InlineData("{\"format\":{\"duration\":\"1e-19\"}}")]
    [InlineData("{\"format\":{\"duration\":\"NaN\"}}")]
    [InlineData("{\"format\":{\"duration\":\"-0.1\"}}")]
    [InlineData("{\"format\":{\"duration\":true}}")]
    [InlineData("{\"streams\":[],\"format\":[]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"disposition\":{\"default\":2}}]}")]
    [InlineData("{\"streams\":[{\"index\":0,\"tags\":{\"title\":{}}}]}")]
    [InlineData("{")]
    public void RejectsMalformedKnownDataInsteadOfCoercingIt(string json)
    {
        Assert.Throws<InvalidDataException>(() => FfprobeJsonReader.Read(json));
    }

    [Fact]
    public void AllowsEmptyValidSectionsAndIgnoresUnknownProperties()
    {
        var asset = FfprobeJsonReader.Read("""
            { "streams": [], "format": {}, "new_extension": { "anything": true } }
            """);

        Assert.Empty(asset.Streams);
        Assert.Null(asset.ReportedDuration);
    }

    [Fact]
    public void RecognizesHlgFromTransferWithoutRequiringMasteringMetadata()
    {
        var stream = Assert.Single(FfprobeJsonReader.Read("""
            { "streams": [{ "index": 1, "codec_type": "video", "color_transfer": "arib-std-b67" }] }
            """).Streams);

        Assert.True(stream.Video!.Color.IsHlg);
        Assert.False(stream.Video.Color.IsPq);
        Assert.Empty(stream.Video.MasteringDisplays);
    }

    [Fact]
    public void RejectsOversizedNumericRepresentationsBeforeBigIntegerParsing()
    {
        var json = "{\"format\":{\"duration\":\"1." + new string('0', 256) + "\"}}";

        var exception = Assert.Throws<InvalidDataException>(() => FfprobeJsonReader.Read(json));

        Assert.Contains("256", exception.Message);
    }
}
