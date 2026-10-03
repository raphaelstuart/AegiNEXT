using Microsoft.Win32.SafeHandles;

namespace AegiNext.Media.Preview;

internal sealed class VideoPreviewHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal VideoPreviewHandle(nint pointer) : base(true)
    {
        SetHandle(pointer);
    }

    protected override bool ReleaseHandle()
    {
        NativePreviewMethods.Destroy(handle);
        return true;
    }
}
