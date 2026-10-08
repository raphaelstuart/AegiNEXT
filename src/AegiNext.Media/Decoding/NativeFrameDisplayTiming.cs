using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeFrameDisplayTiming
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint evidence;
    internal uint reserved;
    internal long timestamp;
    internal int timeBaseNum;
    internal int timeBaseDen;
}
