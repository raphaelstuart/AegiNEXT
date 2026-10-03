using System.Runtime.InteropServices;

namespace AegiNext.Media.Preview;

[StructLayout(LayoutKind.Sequential)]
internal struct NativePreviewRequest
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint width;
    internal uint height;
    internal int colorRange;
    internal int colorMatrix;
    internal int colorPrimaries;
    internal int colorTransfer;
    internal int chromaLocation;
    internal int alphaMode;
    internal uint flags;
    internal uint reserved;
}
