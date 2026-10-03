using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct NativeDecodedFrameInfo
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint width;
    internal uint height;
    internal uint planeCount;
    internal uint componentCount;
    internal uint flags;
    internal uint decodeErrorFlags;
    internal int pixelFormat;
    internal int timeBaseNum;
    internal int timeBaseDen;
    internal int rawFrameTimeBaseNum;
    internal int rawFrameTimeBaseDen;
    internal int streamTimeBaseNum;
    internal int streamTimeBaseDen;
    internal int sampleAspectRatioNum;
    internal int sampleAspectRatioDen;
    internal uint cropLeft;
    internal uint cropTop;
    internal uint cropRight;
    internal uint cropBottom;
    internal fixed uint componentDepth[4];
    internal int colorRange;
    internal int colorMatrix;
    internal int colorPrimaries;
    internal int colorTransfer;
    internal int chromaLocation;
    internal int alphaMode;
    internal uint sideDataCount;
    internal long pts;
    internal long bestEffortTimestamp;
    internal long duration;
    internal fixed byte pixelFormatName[64];
    internal fixed byte colorRangeName[64];
    internal fixed byte colorMatrixName[64];
    internal fixed byte colorPrimariesName[64];
    internal fixed byte colorTransferName[64];
    internal fixed byte chromaLocationName[64];
    internal fixed byte alphaModeName[64];
}
