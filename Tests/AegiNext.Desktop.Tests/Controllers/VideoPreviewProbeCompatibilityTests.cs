using AegiNext.Core.Media;
using AegiNext.Desktop.Controllers;

namespace AegiNext.Desktop.Tests.Controllers;

public sealed class VideoPreviewProbeCompatibilityTests
{
    [Theory]
    [InlineData("Film grain parameters")]
    [InlineData("AV1 FILM GRAIN PARAMETERS")]
    public void Av1FilmGrainReachesTheDecoderForSynthesis(string sideDataType)
    {
        var asset = Asset(sideDataType);

        var media = VideoPreviewProbe.Read(asset);

        Assert.Equal(3, media.VideoStreamIndex);
        Assert.Equal(1920, media.VideoWidth);
        Assert.Equal(1080, media.VideoHeight);
        Assert.Equal(sideDataType, Assert.Single(asset.Streams[0].Video!.SideDataTypes));
    }

    [Theory]
    [InlineData("HDR Dynamic Metadata SMPTE2094-40 (HDR10+)")]
    [InlineData("DYNAMIC HDR10 PLUS")]
    [InlineData("ICC profile")]
    [InlineData("ICC PROFILE")]
    public void UnsupportedColorMetadataStillFailsBeforeDecoding(string sideDataType)
    {
        var error = Assert.Throws<NotSupportedException>(() => VideoPreviewProbe.Read(Asset(sideDataType)));

        Assert.Contains(sideDataType, error.Message, StringComparison.Ordinal);
    }

    private static MediaAssetInfo Asset(string sideDataType)
    {
        return new()
        {
            Streams =
            [
                new()
                {
                    Index = 3,
                    CodecType = "video",
                    CodecName = "av1",
                    Video = new() { Width = 1920, Height = 1080, SideDataTypes = [sideDataType] }
                }
            ]
        };
    }
}
