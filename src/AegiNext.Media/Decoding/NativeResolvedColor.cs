using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeResolvedColor
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint coreVersion;
    internal uint inferredFields;
    internal int range;
    internal int matrix;
    internal int primaries;
    internal int transfer;
    internal int chromaLocation;
    internal int alphaMode;
}
