using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct MacOsRect
{
    internal double X { get; set; }
    internal double Y { get; set; }
    internal double Width { get; set; }
    internal double Height { get; set; }
}
