using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AegiNext.Desktop.Windowing;

[SupportedOSPlatform("windows")]
internal static partial class WindowsNativeMethods
{
    internal const int GWL_STYLE = -16;
    internal const int GWL_EXSTYLE = -20;
    internal const uint WS_CAPTION = 0x00C00000;
    internal const uint WS_MINIMIZE = 0x20000000;
    internal const uint WS_MAXIMIZE = 0x01000000;
    internal const uint WM_NCCALCSIZE = 0x0083;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_NCMOUSEMOVE = 0x00A0;
    internal const uint WM_NCLBUTTONDOWN = 0x00A1;
    internal const uint WM_NCLBUTTONUP = 0x00A2;
    internal const uint WM_NCLBUTTONDBLCLK = 0x00A3;
    internal const uint WM_NCRBUTTONDOWN = 0x00A4;
    internal const uint WM_NCRBUTTONUP = 0x00A5;
    internal const uint WM_NCMOUSELEAVE = 0x02A2;
    internal const uint WM_GETMINMAXINFO = 0x0024;
    internal const uint WM_DPICHANGED = 0x02E0;
    internal const uint WM_DWMCOMPOSITIONCHANGED = 0x031E;
    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOZORDER = 0x0004;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_FRAMECHANGED = 0x0020;
    internal const uint DWMWA_CAPTION_BUTTON_BOUNDS = 5;

    internal static nint GetWindowLongPtr(nint window, int index)
    {
        return IntPtr.Size == sizeof(long) ? GetWindowLongPtr64(window, index) : GetWindowLong32(window, index);
    }

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr64(nint window, int index);

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLong32(nint window, int index);

    [LibraryImport("user32.dll")]
    internal static partial uint GetDpiForWindow(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int AdjustWindowRectExForDpi(ref WindowsRect rect, uint style, int hasMenu,
        uint extendedStyle, uint dpi);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetWindowRect(nint window, out WindowsRect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetClientRect(nint window, out WindowsRect rect);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int ClientToScreen(nint window, ref WindowsPoint point);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int ScreenToClient(nint window, ref WindowsPoint point);

    [LibraryImport("user32.dll")]
    internal static partial int IsWindowVisible(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int IsIconic(nint window);

    [LibraryImport("user32.dll")]
    internal static partial int IsZoomed(nint window);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height,
        uint flags);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int GetWindowPlacement(nint window, ref WindowsWindowPlacement placement);

    [LibraryImport("user32.dll", SetLastError = true)]
    internal static partial int SetWindowPlacement(nint window, in WindowsWindowPlacement placement);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmExtendFrameIntoClientArea(nint window, in WindowsMargins margins);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmGetWindowAttribute(nint window, uint attribute, out WindowsRect rect, uint size);

    [LibraryImport("dwmapi.dll")]
    internal static partial int DwmDefWindowProc(nint window, uint message, nint wParam, nint lParam, out nint result);
}
