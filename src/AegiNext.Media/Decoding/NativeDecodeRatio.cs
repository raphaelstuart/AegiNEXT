using System.Runtime.InteropServices;

namespace AegiNext.Media.Decoding;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeDecodeRatio
{
    internal long numerator;
    internal long denominator;
}
