using Microsoft.Win32.SafeHandles;

namespace AegiNext.Media.Decoding;

internal sealed class DecodedFrameHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal DecodedFrameHandle(nint pointer) : base(true)
    {
        SetHandle(pointer);
    }

    protected override bool ReleaseHandle()
    {
        NativeDecodeMethods.DestroyFrame(handle);
        return true;
    }
}
