using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDecodeBackendInfo
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint compileAvformat;
    internal uint runtimeAvformat;
    internal uint compileAvcodec;
    internal uint runtimeAvcodec;
    internal uint compileAvutil;
    internal uint runtimeAvutil;
    internal fixed byte releaseVersion[32];
}
