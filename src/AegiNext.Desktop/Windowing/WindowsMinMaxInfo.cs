using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsMinMaxInfo
{
    internal WindowsPoint Reserved { get; set; }
    internal WindowsPoint MaxSize { get; set; }
    internal WindowsPoint MaxPosition { get; set; }
    internal WindowsPoint MinTrackSize { get; set; }
    internal WindowsPoint MaxTrackSize { get; set; }
}
