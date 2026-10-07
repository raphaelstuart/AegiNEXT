using System.Runtime.InteropServices;

namespace AegiNext.Media.Encoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExportOverlayInfo
{
    internal uint StructSize;
    internal uint AbiVersion;
    internal NativeExportOverlayState State;
    internal uint Reserved;
    internal ulong Revision;
}
