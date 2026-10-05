using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDecoderSessionInfo
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint coreVersion;
    internal uint capabilities;
    internal uint requestedMode;
    internal uint activeBackend;
    internal uint hardwareConfirmed;
    internal uint reserved;
    internal ulong generation;
    internal ulong deliveredFrames;
    internal ulong decodeNanoseconds;
    internal ulong downloadNanoseconds;
    internal fixed byte fallbackReason[256];
}
