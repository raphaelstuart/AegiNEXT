using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDecoderOptions
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint mode;
    internal uint workload;
}
