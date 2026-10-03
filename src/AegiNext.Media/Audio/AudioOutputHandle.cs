using Microsoft.Win32.SafeHandles;

namespace AegiNext.Media.Audio;

internal sealed class AudioOutputHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal AudioOutputHandle(nint value) : base(true)
    {
        SetHandle(value);
    }

    protected override bool ReleaseHandle()
    {
        NativeAudioMethods.DestroyOutput(handle);
        return true;
    }
}

