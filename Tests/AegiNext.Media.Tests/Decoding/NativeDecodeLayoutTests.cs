using System.Runtime.InteropServices;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Decoding;

public sealed class NativeDecodeLayoutTests
{
    private static readonly string[] acceptedReleaseVersions = ["9.0.2", "9.0.2-full_build-www.gyan.dev"];

    [DecoderFact]
    [Trait("Category", "DecoderIntegration")]
    public void NativeAndManagedLayoutsMatchVersionOne()
    {
        Assert.Equal(64, Marshal.SizeOf<NativeDecodeBackendInfo>());
        Assert.Equal(32, Marshal.OffsetOf<NativeDecodeBackendInfo>("releaseVersion").ToInt32());
        Assert.Equal(600, Marshal.SizeOf<NativeDecodedFrameInfo>());
        Assert.Equal(128, Marshal.OffsetOf<NativeDecodedFrameInfo>("pts").ToInt32());
        Assert.Equal(136, Marshal.OffsetOf<NativeDecodedFrameInfo>("bestEffortTimestamp").ToInt32());
        Assert.Equal(144, Marshal.OffsetOf<NativeDecodedFrameInfo>("duration").ToInt32());
        Assert.Equal(152, Marshal.OffsetOf<NativeDecodedFrameInfo>("pixelFormatName").ToInt32());
        Assert.Equal(32, Marshal.SizeOf<NativeDecodedPlaneInfo>());
        Assert.Equal(16, Marshal.OffsetOf<NativeDecodedPlaneInfo>("nativeStride").ToInt32());
        Assert.Equal(20, Marshal.OffsetOf<NativeDecodedPlaneInfo>("rowBytes").ToInt32());
        Assert.Equal(24, Marshal.OffsetOf<NativeDecodedPlaneInfo>("tightByteCount").ToInt32());
        Assert.Equal(16, Marshal.SizeOf<NativeDecodeRatio>());
        Assert.Equal(192, Marshal.SizeOf<NativeDecodedHdrInfo>());
        Assert.Equal(16, Marshal.OffsetOf<NativeDecodedHdrInfo>("redX").ToInt32());
        Assert.Equal(144, Marshal.OffsetOf<NativeDecodedHdrInfo>("minLuminance").ToInt32());
        Assert.Equal(160, Marshal.OffsetOf<NativeDecodedHdrInfo>("maxLuminance").ToInt32());
        Assert.Equal(176, Marshal.OffsetOf<NativeDecodedHdrInfo>("maxContentLightLevel").ToInt32());
        Assert.Equal(184, Marshal.OffsetOf<NativeDecodedHdrInfo>("reserved2").ToInt32());

        var backend = FfmpegVideoDecoder.GetBackendInfo();
        Assert.Contains(backend.ReleaseVersion, acceptedReleaseVersions);
        Assert.Equal(new Version(63, 1, 102), backend.AvFormatVersion);
        Assert.Equal(new Version(63, 1, 102), backend.AvCodecVersion);
        Assert.Equal(new Version(61, 1, 102), backend.AvUtilVersion);
    }
}
