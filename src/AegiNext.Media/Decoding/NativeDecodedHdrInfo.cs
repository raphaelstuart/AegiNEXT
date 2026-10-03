using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDecodedHdrInfo
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint flags;
    internal uint reserved;
    internal NativeDecodeRatio redX;
    internal NativeDecodeRatio redY;
    internal NativeDecodeRatio greenX;
    internal NativeDecodeRatio greenY;
    internal NativeDecodeRatio blueX;
    internal NativeDecodeRatio blueY;
    internal NativeDecodeRatio whitePointX;
    internal NativeDecodeRatio whitePointY;
    internal NativeDecodeRatio minLuminance;
    internal NativeDecodeRatio maxLuminance;
    internal uint maxContentLightLevel;
    internal uint maxFrameAverageLightLevel;
    internal ulong reserved2;
}
