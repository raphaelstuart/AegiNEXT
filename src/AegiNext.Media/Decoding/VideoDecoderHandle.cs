using Microsoft.Win32.SafeHandles;

namespace AegiNext.Media.Decoding;

internal sealed class VideoDecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal VideoDecoderHandle(nint pointer) : base(true)
    {
        SetHandle(pointer);
    }

    protected override bool ReleaseHandle()
    {
        NativeDecodeMethods.Destroy(handle);
        return true;
    }
}
