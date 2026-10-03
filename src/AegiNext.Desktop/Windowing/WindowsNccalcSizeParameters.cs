using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsNccalcSizeParameters
{
    internal WindowsRect NewWindow { get; set; }
    internal WindowsRect OldWindow { get; set; }
    internal WindowsRect OldClient { get; set; }
    internal nint WindowPosition { get; set; }
}
