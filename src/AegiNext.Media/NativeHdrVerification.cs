using System.Runtime.InteropServices;

namespace AegiNext.Media;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeHdrVerification
{
    internal uint structSize;
    internal uint abiVersion;
    internal uint pipelineOk;
    internal uint reserved;
    internal float uploadMaxError;
    internal float primariesMaxError;
    internal float referenceWhiteMaxError;
    internal float hdrMaxComponent;
}
