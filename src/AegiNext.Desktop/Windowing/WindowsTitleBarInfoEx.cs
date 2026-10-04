using System.Runtime.InteropServices;

namespace AegiNext.Desktop.Windowing;

[StructLayout(LayoutKind.Sequential)]
internal struct WindowsTitleBarInfoEx
{
    internal uint Size { get; set; }
    internal WindowsRect TitleBounds { get; set; }
    internal uint TitleState { get; set; }
    internal uint ReservedState { get; set; }
    internal uint MinimizeState { get; set; }
    internal uint MaximizeState { get; set; }
    internal uint HelpState { get; set; }
    internal uint CloseState { get; set; }
    internal WindowsRect ReservedBounds0 { get; set; }
    internal WindowsRect ReservedBounds1 { get; set; }
    internal WindowsRect MinimizeBounds { get; set; }
    internal WindowsRect MaximizeBounds { get; set; }
    internal WindowsRect HelpBounds { get; set; }
    internal WindowsRect CloseBounds { get; set; }
}
