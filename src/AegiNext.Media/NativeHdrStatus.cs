using System.Runtime.InteropServices;

namespace AegiNext.Media;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeHdrStatus
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint drawableWidth;
    internal uint drawableHeight;
    internal uint float16Verified;
    internal uint edrEnabled;
    internal uint displayId;
    internal uint reserved;
    internal float currentHeadroom;
    internal float potentialHeadroom;
    internal float backingScale;
    internal float nominalDisplayWhite;
    internal ulong submittedFrames;
    internal float sourceWhiteNits;
    internal float sourcePeakNits;
    internal uint liveContexts;
    internal uint reserved2;
}
