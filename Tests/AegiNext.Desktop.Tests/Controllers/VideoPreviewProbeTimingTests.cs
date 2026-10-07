using AegiNext.Core.Media;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controllers;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class VideoPreviewProbeTimingTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(3000, 3000, 3000)]
    [InlineData(3100, 3000, 3000)]
    [InlineData(3000, 3200, 3000)]
    [InlineData(-100, -200, -200)]
    public void PlaybackOriginUsesContainerStartWhileVideoOriginRetainsItsOwnStart(long videoStart, long audioStart, long containerStart)
    {
        var asset = Asset(videoStart, audioStart) with { ReportedStart = new(containerStart, 1000) };

        var media = VideoPreviewProbe.Read(asset);

        Assert.Equal(new MediaTime(videoStart, 1000), media.Start);
        Assert.Equal(new MediaTime(containerStart, 1000), media.PlaybackOrigin);
    }

    [Theory]
    [InlineData(3100, 3000, 3000)]
    [InlineData(3000, 3200, 3000)]
    [InlineData(-100, -200, -200)]
    public void MissingContainerOriginUsesKnownRawStreamTimes(long videoStart, long audioStart, long expected)
    {
        Assert.Equal(new MediaTime(expected, 1000), VideoPreviewProbe.Read(Asset(videoStart, audioStart)).PlaybackOrigin);
    }

    [Fact]
    public void EntirelyUnknownTimingRemainsUnknown()
    {
        var media = VideoPreviewProbe.Read(new()
        {
            Streams = [new() { Index = 0, CodecType = "video", Video = new() { Width = 64, Height = 48 } }]
        });

        Assert.Null(media.PlaybackOrigin);
        Assert.Null(media.Start);
    }

    private static MediaAssetInfo Asset(long videoStart, long audioStart)
    {
        return new()
        {
            Streams =
            [
                new() { Index = 0, CodecType = "video", Video = new() { Width = 64, Height = 48 }, Timing = Timing(videoStart) },
                new() { Index = 1, CodecType = "audio", Timing = Timing(audioStart) }
            ]
        };
    }

    private static MediaStreamTiming Timing(long start)
    {
        return new() { StartPts = start, TimeBase = new(1, 1000), ReportedStart = new(9) };
    }
}
