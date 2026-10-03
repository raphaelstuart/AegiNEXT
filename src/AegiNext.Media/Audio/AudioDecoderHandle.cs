using Microsoft.Win32.SafeHandles;

namespace AegiNext.Media.Audio;

internal sealed class AudioDecoderHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal AudioDecoderHandle(nint value) : base(true)
    {
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        NativeAudioMethods.DestroyDecoder(handle);
        return true;
    }
}

