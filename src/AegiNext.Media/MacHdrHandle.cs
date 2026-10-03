using Microsoft.Win32.SafeHandles;

namespace AegiNext.Media;

internal sealed class MacHdrHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal MacHdrHandle(nint pointer) : base(true)
    {
        SetHandle(pointer);
    }

    protected override bool ReleaseHandle()
    {
        NativeHdrMethods.Destroy(handle);
        return true;
    }
}
