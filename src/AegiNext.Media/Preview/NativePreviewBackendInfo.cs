using System.Runtime.InteropServices;

namespace AegiNext.Media.Preview;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePreviewBackendInfo
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint compileSwscale;
    internal uint runtimeSwscale;
}
