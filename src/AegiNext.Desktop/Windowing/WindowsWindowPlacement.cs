using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsWindowPlacement
{
    internal uint Length { get; set; }
    internal uint Flags { get; set; }
    internal uint ShowCommand { get; set; }
    internal WindowsPoint MinPosition { get; set; }
    internal WindowsPoint MaxPosition { get; set; }
    internal WindowsRect NormalPosition { get; set; }
}
