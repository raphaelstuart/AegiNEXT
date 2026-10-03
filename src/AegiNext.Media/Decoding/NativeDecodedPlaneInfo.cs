using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDecodedPlaneInfo
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint planeIndex;
    internal uint rows;
    internal int nativeStride;
    internal uint rowBytes;
    internal ulong tightByteCount;
}
