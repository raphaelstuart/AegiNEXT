using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsRect
{
    internal int Left { get; set; }
    internal int Top { get; set; }
    internal int Right { get; set; }
    internal int Bottom { get; set; }
}
