using System.Runtime.InteropServices;

namespace AegiNext.Media;

internal static partial class NativeHdrMethods
{
    static NativeHdrMethods()
    {
        NativeMediaRuntime.Initialize();
    }

    internal const uint ABI_VERSION = 1;
    internal const int NOT_READY = 5;
    private const string LIBRARY = "aeginext_media";

    [LibraryImport(LIBRARY, EntryPoint = "an_hdr_abi_version")]
    internal static partial uint AbiVersion();

    [LibraryImport(LIBRARY, EntryPoint = "an_hdr_live_contexts")]
    internal static partial uint LiveContexts();

    [LibraryImport(LIBRARY, EntryPoint = "an_hdr_create")]
    internal static unsafe partial int Create(out nint context, out nint view, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_hdr_destroy")]
    internal static partial void Destroy(nint context);

    [LibraryImport(LIBRARY, EntryPoint = "an_hdr_present")]
    internal static unsafe partial int Present(MacHdrHandle context, Half* rgba, ulong byteCount, uint width,
        uint height, uint rowBytes, float sourceWhiteNits, float sourcePeakNits, ref NativeHdrStatus status,
        byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_hdr_verify")]
    internal static unsafe partial int Verify(MacHdrHandle context, ref NativeHdrVerification result, byte* error, uint capacity);
}
