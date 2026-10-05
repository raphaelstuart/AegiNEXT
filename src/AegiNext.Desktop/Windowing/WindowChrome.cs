using AegiNext.Desktop.Controls.Common;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Platform;

namespace AegiNext.Desktop.Windowing;

internal static class WindowChrome
{
    internal static IWindowChrome Attach(Window window, WindowTitleBar titleBar)
    {
        titleBar.Bind(WindowTitleBar.TitleProperty, new Binding(nameof(Window.Title)) { Source = window });
        window.WindowDecorations = WindowDecorations.Full;
        if (OperatingSystem.IsWindows() && window.TryGetPlatformHandle() is { HandleDescriptor: "HWND" })
        {
            return new WindowsWindowChrome(window, titleBar);
        }

        if (OperatingSystem.IsMacOS() && window.TryGetPlatformHandle() is IPlatformHandle { HandleDescriptor: "NSWindow", Handle: not 0 })
        {
            return new MacOsWindowChrome(window, titleBar);
        }

        return new ManagedWindowChrome(window);
    }

    internal static void ValidateClientSize(Size size)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), "客户区尺寸必须是有限正值。");
        }
    }
}
