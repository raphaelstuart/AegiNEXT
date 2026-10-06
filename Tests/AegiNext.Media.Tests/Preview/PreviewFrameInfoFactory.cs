using System.Collections.Immutable;
using System.Text;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Tests.Preview;

internal static class PreviewFrameInfoFactory
{
    internal static unsafe VideoFrameInfo Create(
        int range = 1,
        int matrix = 1,
        int primaries = 1,
        int transfer = 1,
        int chroma = 1,
        int alpha = 0,
        string pixelFormat = "yuv420p",
        uint componentCount = 3,
        uint depth = 8,
        uint flags = 0,
        ImmutableArray<string> sideDataTypes = default,
        NativeDecodedHdrInfo hdr = default)
    {
        var value = new NativeDecodedFrameInfo
        {
            width = 8,
            height = 4,
            planeCount = 3,
            componentCount = componentCount,
            colorRange = range,
            colorMatrix = matrix,
            colorPrimaries = primaries,
            colorTransfer = transfer,
            chromaLocation = chroma,
            alphaMode = alpha,
            sampleAspectRatioNum = 1,
            sampleAspectRatioDen = 1,
            flags = flags
        };
        for (var index = 0; index < componentCount; index++)
        {
            value.componentDepth[index] = depth;
        }

        System.Text.Encoding.UTF8.GetBytes(pixelFormat, new Span<byte>(value.pixelFormatName, NativeDecodeMethods.NAME_CAPACITY));
        return new(value, hdr, sideDataTypes.IsDefault ? [] : sideDataTypes);
    }
}
