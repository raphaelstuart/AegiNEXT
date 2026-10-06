using System.Runtime.InteropServices;

namespace AegiNext.Media.Tests;

public class NativeHdrLayoutTests
{
    [Fact]
    public void StatusMatchesTheNativeAbiSizeAndAlignedFrameCounter()
    {
        Assert.Equal(72, Marshal.SizeOf<NativeHdrStatus>());
        Assert.Equal(48, Marshal.OffsetOf<NativeHdrStatus>("submittedFrames").ToInt32());
        Assert.Equal(56, Marshal.OffsetOf<NativeHdrStatus>("sourceWhiteNits").ToInt32());
    }

    [Fact]
    public void VerificationMatchesTheNativeAbiSizeAndFinalFloat()
    {
        Assert.Equal(32, Marshal.SizeOf<NativeHdrVerification>());
        Assert.Equal(28, Marshal.OffsetOf<NativeHdrVerification>("hdrMaxComponent").ToInt32());
    }
}
