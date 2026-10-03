using System.Runtime.InteropServices;

namespace AegiNext.Media.Encoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeExportRequest
{
    internal uint StructSize;
    internal uint AbiVersion;
    internal int VideoStreamIndex;
    internal int Codec;
    internal int Crf;
    internal uint Width;
    internal uint Height;
    internal uint Flags;
    internal nint InputPath;
    internal nint OutputPath;
    internal nint Preset;
    internal float ReferenceWhiteNits;
    internal uint Reserved;
}
