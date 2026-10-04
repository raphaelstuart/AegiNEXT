using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace AegiNext.Media.Encoding;

internal static partial class NativeExportMethods
{
    static NativeExportMethods()
    {
        NativeMediaRuntime.Initialize();
    }

    private const string LIBRARY = "aeginext_export";

    [LibraryImport(LIBRARY, EntryPoint = "an_export_abi_version")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial uint AbiVersion();

    [LibraryImport(LIBRARY, EntryPoint = "an_export_create")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Create(out nint context, byte* error, uint capacity);

    [LibraryImport(LIBRARY, EntryPoint = "an_export_destroy")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Destroy(nint context);

    [LibraryImport(LIBRARY, EntryPoint = "an_export_cancel")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void Cancel(nint context);

    [LibraryImport(LIBRARY, EntryPoint = "an_export_run")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static unsafe partial int Run(nint context, ref NativeExportRequest request,
        delegate* unmanaged[Cdecl]<nint, long, int, int, uint, uint, float*, ulong, int> callback,
        nint user, out ulong frames, byte* error, uint capacity);
}
