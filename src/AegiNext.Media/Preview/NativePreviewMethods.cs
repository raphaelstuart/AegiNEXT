using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

internal static partial class NativePreviewMethods
{
    internal const uint FEATURE = 2;
    private const string LIBRARY = "aeginext_decode";

    [LibraryImport(LIBRARY, EntryPoint = "an_preview_get_backend_info")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int GetBackendInfo(ref NativePreviewBackendInfo info, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_preview_converter_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Create(out nint converter, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_preview_converter_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Destroy(nint converter);

    [LibraryImport(LIBRARY, EntryPoint = "an_preview_live_converters")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint LiveConverters();

    [LibraryImport(LIBRARY, EntryPoint = "an_preview_convert")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Convert(VideoPreviewHandle converter, DecodedFrameHandle frame,
        ref NativePreviewRequest request, byte* destination, ulong destinationCapacity, byte* error, uint errorCapacity);
}
