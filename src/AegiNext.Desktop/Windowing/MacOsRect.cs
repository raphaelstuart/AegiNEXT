using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct MacOsRect
{
    internal double X;
    internal double Y;
    internal double Width;
    internal double Height;
}
