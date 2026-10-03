using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsPoint
{
    internal int X { get; set; }
    internal int Y { get; set; }
}
