using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using AegiNext.Desktop.Controls.Common;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;

namespace AegiNext.Desktop.Windowing;

[SupportedOSPlatform("windows")]
internal sealed class WindowsWindowChrome : IWindowChrome
{
    private readonly Window window;
    private readonly WindowTitleBar titleBar;
    private readonly nint handle;
    private readonly Win32Properties.CustomWndProcHookCallback callback;
    private readonly double minimumTitleBarHeight;
    private readonly WindowsCaptionSurface captionSurface;
    private Size? openingClientSize;
    private Size? deferredClientSize;
    private bool disposed;
    private bool refreshQueued;
    private bool frameRefreshRequested;
    private bool applyingFrame;

    internal WindowsWindowChrome(Window window, WindowTitleBar titleBar)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(titleBar);
        var platformHandle = window.TryGetPlatformHandle();
        if (platformHandle is not { HandleDescriptor: "HWND", Handle: not 0 })
        {
            throw new ArgumentException("Windows 标题栏必须附加到真实 HWND。", nameof(window));
        }

        this.window = window;
        this.titleBar = titleBar;
        handle = platformHandle.Handle;
        minimumTitleBarHeight = titleBar.Height;
        if (!double.IsFinite(minimumTitleBarHeight) || minimumTitleBarHeight <= 0)
        {
            throw new ArgumentException("标题栏必须提供正数高度。", nameof(titleBar));
        }

        if (window.SizeToContent == SizeToContent.Manual && double.IsFinite(window.Width) && window.Width > 0 &&
            double.IsFinite(window.Height) && window.Height > 0)
        {
            openingClientSize = new(window.Width, window.Height);
        }

        window.WindowDecorations = WindowDecorations.Full;
        window.ExtendClientAreaToDecorationsHint = false;
        captionSurface = new(window);
        callback = WindowProcedure;
        Win32Properties.AddWndProcHookCallback(window, callback);
        window.Opened += OnOpened;
        window.Closed += OnClosed;
        window.Resized += OnResized;
        window.LayoutUpdated += OnLayoutUpdated;
        window.ScalingChanged += OnScalingChanged;
        window.PropertyChanged += OnWindowPropertyChanged;
        titleBar.PropertyChanged += OnTitleBarPropertyChanged;
        try
        {
            ApplyFrame();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal Exception? LastError { get; private set; }
    internal bool CaptionButtonsMeasured { get; private set; }
    internal bool NonClientRenderingEnabled { get; private set; }
    internal Rect CaptionAperture => captionSurface.Aperture;

    /// <inheritdoc />
    public void ResizeClient(Size size)
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(disposed, this);
        if (LastError is { } error)
        {
            throw new InvalidOperationException("原生 Windows 标题栏已经失败，不能提交客户区尺寸。", error);
        }

        var outerSize = WindowsChromeGeometry.GetOuterSize(size, ReadDpi(), ReadFrameInsets());
        if (window.WindowState == WindowState.FullScreen)
        {
            deferredClientSize = size;
            return;
        }

        if (WindowsNativeMethods.IsIconic(handle) != 0 || WindowsNativeMethods.IsZoomed(handle) != 0)
        {
            var placement = new WindowsWindowPlacement
            {
                Length = checked((uint)Marshal.SizeOf<WindowsWindowPlacement>())
            };
            CheckWin32(WindowsNativeMethods.GetWindowPlacement(handle, ref placement));
            var normal = placement.NormalPosition;
            normal.Right = checked(normal.Left + outerSize.Width);
            normal.Bottom = checked(normal.Top + outerSize.Height);
            placement.NormalPosition = normal;
            CheckWin32(WindowsNativeMethods.SetWindowPlacement(handle, in placement));
        }
        else
        {
            CheckWin32(WindowsNativeMethods.SetWindowPos(handle, 0, 0, 0, outerSize.Width, outerSize.Height,
                WindowsNativeMethods.SWP_NOMOVE | WindowsNativeMethods.SWP_NOZORDER |
                WindowsNativeMethods.SWP_NOACTIVATE));
        }

        QueueRefresh(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Win32Properties.RemoveWndProcHookCallback(window, callback);
        window.Opened -= OnOpened;
        window.Closed -= OnClosed;
        window.Resized -= OnResized;
        window.LayoutUpdated -= OnLayoutUpdated;
        window.ScalingChanged -= OnScalingChanged;
        window.PropertyChanged -= OnWindowPropertyChanged;
        titleBar.PropertyChanged -= OnTitleBarPropertyChanged;
        captionSurface.Dispose();
    }

    private unsafe nint WindowProcedure(nint nativeWindow, uint message, nint wParam, nint lParam, ref bool handled)
    {
        if (disposed || LastError is not null)
        {
            return 0;
        }

        try
        {
            switch (message)
            {
                case WindowsNativeMethods.WM_NCCALCSIZE:
                    if (window.WindowState != WindowState.FullScreen && WindowsNativeMethods.IsIconic(handle) == 0)
                    {
                        var frame = ReadFrameInsets();
                        var maximized = WindowsNativeMethods.IsZoomed(handle) != 0;
                        if (wParam != 0)
                        {
                            var parameters = (WindowsNccalcSizeParameters*)lParam;
                            parameters->NewWindow = WindowsChromeGeometry.GetClientRect(parameters->NewWindow, frame,
                                maximized);
                        }
                        else
                        {
                            var rect = (WindowsRect*)lParam;
                            *rect = WindowsChromeGeometry.GetClientRect(*rect, frame, maximized);
                        }

                        handled = true;
                    }

                    return 0;
                case WindowsNativeMethods.WM_GETMINMAXINFO:
                    if (window.WindowState != WindowState.FullScreen)
                    {
                        ApplyTrackingSizes((WindowsMinMaxInfo*)lParam);
                        handled = true;
                    }

                    return 0;
                case WindowsNativeMethods.WM_NCHITTEST:
                    if (window.WindowState == WindowState.FullScreen)
                    {
                        return 0;
                    }

                    handled = true;
                    return HitTest(nativeWindow, message, wParam, lParam);
                case WindowsNativeMethods.WM_NCMOUSEMOVE:
                case WindowsNativeMethods.WM_NCLBUTTONDOWN:
                case WindowsNativeMethods.WM_NCLBUTTONUP:
                case WindowsNativeMethods.WM_NCLBUTTONDBLCLK:
                case WindowsNativeMethods.WM_NCRBUTTONDOWN:
                case WindowsNativeMethods.WM_NCRBUTTONUP:
                case WindowsNativeMethods.WM_NCMOUSELEAVE:
                    if (WindowsNativeMethods.DwmDefWindowProc(nativeWindow, message, wParam, lParam, out var result) != 0)
                    {
                        handled = true;
                        return result;
                    }

                    return 0;
                case WindowsNativeMethods.WM_DPICHANGED:
                case WindowsNativeMethods.WM_ACTIVATE:
                case WindowsNativeMethods.WM_DWMCOMPOSITIONCHANGED:
                case WindowsNativeMethods.WM_THEMECHANGED:
                    QueueRefresh(true);
                    return 0;
                default:
                    return 0;
            }
        }
        catch (Exception error) when (error is Win32Exception or COMException or OverflowException or ArgumentException)
        {
            LastError = error;
            handled = false;
            Trace.TraceError("Windows 原生标题栏失败：{0}", error);
            return 0;
        }
    }

    private unsafe nint HitTest(nint nativeWindow, uint message, nint wParam, nint lParam)
    {
        if (WindowsNativeMethods.DwmDefWindowProc(nativeWindow, message, wParam, lParam, out var result) != 0 &&
            result != (nint)WindowsChromeHitTest.CLIENT && result != (nint)WindowsChromeHitTest.NONE)
        {
            return result;
        }

        var point = WindowsChromeGeometry.GetScreenPoint(lParam);
        var nativeTitleBar = new WindowsTitleBarInfoEx { Size = checked((uint)sizeof(WindowsTitleBarInfoEx)) };
        WindowsNativeMethods.DefWindowProc(handle, WindowsNativeMethods.WM_GETTITLEBARINFOEX, 0,
            (nint)(&nativeTitleBar));
        var captionHit = WindowsChromeGeometry.HitCaptionButtons(point, in nativeTitleBar);
        if (captionHit != WindowsChromeHitTest.NONE)
        {
            return (nint)captionHit;
        }

        CheckWin32(WindowsNativeMethods.GetWindowRect(handle, out var outer));
        var resize = WindowsChromeGeometry.HitResizeFrame(point, outer, ReadFrameInsets(), window.CanResize,
            WindowsNativeMethods.IsZoomed(handle) != 0);
        if (resize != WindowsChromeHitTest.NONE)
        {
            return (nint)resize;
        }

        CheckWin32(WindowsNativeMethods.ScreenToClient(handle, ref point));
        var scale = ReadDpi() / WindowsChromeGeometry.STANDARD_DPI;
        return titleBar.IsDragRegion(new(point.X / scale, point.Y / scale))
            ? (nint)WindowsChromeHitTest.CAPTION
            : (nint)WindowsChromeHitTest.CLIENT;
    }

    private unsafe void ApplyTrackingSizes(WindowsMinMaxInfo* sizes)
    {
        var frame = ReadFrameInsets();
        var dpi = ReadDpi();
        var minimum = sizes->MinTrackSize;
        var maximum = sizes->MaxTrackSize;
        if (window.MinWidth > 0)
        {
            minimum.X = checked(WindowsChromeGeometry.ToPixels(window.MinWidth, dpi) + frame.Left + frame.Right);
        }

        if (window.MinHeight > 0)
        {
            minimum.Y = checked(WindowsChromeGeometry.ToPixels(window.MinHeight, dpi) + frame.Bottom);
        }

        if (double.IsFinite(window.MaxWidth) && window.MaxWidth > 0)
        {
            maximum.X = checked(WindowsChromeGeometry.ToPixels(window.MaxWidth, dpi) + frame.Left + frame.Right);
        }

        if (double.IsFinite(window.MaxHeight) && window.MaxHeight > 0)
        {
            maximum.Y = checked(WindowsChromeGeometry.ToPixels(window.MaxHeight, dpi) + frame.Bottom);
        }

        sizes->MinTrackSize = minimum;
        sizes->MaxTrackSize = maximum;
    }

    private uint ReadDpi()
    {
        var dpi = WindowsNativeMethods.GetDpiForWindow(handle);
        if (dpi == 0)
        {
            throw new Win32Exception("无法读取 Windows 窗口 DPI。");
        }

        return dpi;
    }

    private WindowsFrameInsets ReadFrameInsets()
    {
        var style = unchecked((uint)WindowsNativeMethods.GetWindowLongPtr(handle, WindowsNativeMethods.GWL_STYLE));
        var extendedStyle = unchecked((uint)WindowsNativeMethods.GetWindowLongPtr(handle,
            WindowsNativeMethods.GWL_EXSTYLE));
        style &= ~(WindowsNativeMethods.WS_CAPTION | WindowsNativeMethods.WS_MINIMIZE | WindowsNativeMethods.WS_MAXIMIZE);
        var frame = new WindowsRect();
        CheckWin32(WindowsNativeMethods.AdjustWindowRectExForDpi(ref frame, style, 0, extendedStyle, ReadDpi()));
        return new(-frame.Left, -frame.Top, frame.Right, frame.Bottom);
    }

    private void ApplyFrame()
    {
        applyingFrame = true;
        try
        {
            CheckWin32(WindowsNativeMethods.SetWindowPos(handle, 0, 0, 0, 0, 0,
                WindowsNativeMethods.SWP_NOMOVE | WindowsNativeMethods.SWP_NOSIZE | WindowsNativeMethods.SWP_NOZORDER |
                WindowsNativeMethods.SWP_NOACTIVATE | WindowsNativeMethods.SWP_FRAMECHANGED));
            var policy = WindowsNativeMethods.DWMNCRP_ENABLED;
            Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmSetWindowAttribute(handle,
                WindowsNativeMethods.DWMWA_NCRENDERING_POLICY, in policy, sizeof(int)));
            var dark = window.ActualThemeVariant == ThemeVariant.Dark ? 1 : 0;
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmSetWindowAttribute(handle,
                    WindowsNativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, in dark, sizeof(int)));
            }

            var margins = new WindowsMargins
            {
                TopHeight = window.WindowState == WindowState.FullScreen
                    ? 0
                    : WindowsChromeGeometry.ToPixels(titleBar.Height, ReadDpi())
            };
            Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmExtendFrameIntoClientArea(handle, in margins));
            Marshal.ThrowExceptionForHR(WindowsNativeMethods.DwmGetWindowAttribute(handle,
                WindowsNativeMethods.DWMWA_NCRENDERING_ENABLED, out int enabled, sizeof(int)));
            NonClientRenderingEnabled = enabled != 0;
        }
        finally
        {
            applyingFrame = false;
        }
    }

    private void RefreshCaptionGeometry()
    {
        if (window.WindowState == WindowState.FullScreen)
        {
            captionSurface.Update(default);
            return;
        }

        if (WindowsNativeMethods.IsWindowVisible(handle) == 0 || WindowsNativeMethods.IsIconic(handle) != 0 ||
            window.ClientSize.Width <= 0 || window.ClientSize.Height <= 0)
        {
            return;
        }

        var status = WindowsNativeMethods.DwmGetWindowAttribute(handle, WindowsNativeMethods.DWMWA_CAPTION_BUTTON_BOUNDS,
            out WindowsRect bounds, checked((uint)Marshal.SizeOf<WindowsRect>()));
        CaptionButtonsMeasured = status >= 0 && WindowsChromeGeometry.IsUsableCaptionBounds(bounds);
        if (!CaptionButtonsMeasured)
        {
            return;
        }

        CheckWin32(WindowsNativeMethods.GetWindowRect(handle, out var outer));
        CheckWin32(WindowsNativeMethods.GetClientRect(handle, out var client));
        var origin = new WindowsPoint();
        CheckWin32(WindowsNativeMethods.ClientToScreen(handle, ref origin));
        var dpi = ReadDpi();
        titleBar.CaptionInsets = WindowsChromeGeometry.GetCaptionInsets(outer, origin, client.Right - client.Left,
            bounds, dpi);
        var captionBottom = (long)outer.Top + bounds.Bottom - origin.Y;
        titleBar.Height = Math.Max(minimumTitleBarHeight, captionBottom * WindowsChromeGeometry.STANDARD_DPI / dpi);
        captionSurface.Update(WindowsChromeGeometry.GetCaptionAperture(outer, origin, window.ClientSize, bounds, dpi));
    }

    private void QueueRefresh(bool frame)
    {
        if (disposed)
        {
            return;
        }

        frameRefreshRequested |= frame;
        if (refreshQueued)
        {
            return;
        }

        refreshQueued = true;
        Dispatcher.UIThread.Post(RefreshQueued, DispatcherPriority.Loaded);
    }

    private void RefreshQueued()
    {
        refreshQueued = false;
        if (disposed || LastError is not null)
        {
            return;
        }

        var applyFrame = frameRefreshRequested;
        frameRefreshRequested = false;
        try
        {
            if (applyFrame)
            {
                ApplyFrame();
            }

            if (deferredClientSize is { } size && window.WindowState == WindowState.Normal)
            {
                deferredClientSize = null;
                ResizeClient(size);
            }

            RefreshCaptionGeometry();
            FitClientToContent();
        }
        catch (Exception error) when (error is Win32Exception or COMException or OverflowException or ArgumentException)
        {
            LastError = error;
            Trace.TraceError("Windows 原生标题栏刷新失败：{0}", error);
        }
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        if (openingClientSize is { } size)
        {
            openingClientSize = null;
            ResizeClient(size);
        }

        QueueRefresh(true);
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private void OnResized(object? sender, WindowResizedEventArgs e)
    {
        if (!applyingFrame)
        {
            QueueRefresh(true);
        }
    }

    private void OnScalingChanged(object? sender, EventArgs e)
    {
        QueueRefresh(true);
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (TryGetContentClientSize(out _))
        {
            QueueRefresh(false);
        }
    }

    private void FitClientToContent()
    {
        if (!TryGetContentClientSize(out var size))
        {
            return;
        }

        var sizing = window.SizeToContent;
        ResizeClient(size);
        window.SetCurrentValue(Window.SizeToContentProperty, sizing);
    }

    private bool TryGetContentClientSize(out Size size)
    {
        size = default;
        if (disposed || LastError is not null || !window.IsVisible || window.WindowState != WindowState.Normal ||
            window.SizeToContent == SizeToContent.Manual || window.Content is not Control content ||
            !content.IsMeasureValid || !content.IsArrangeValid)
        {
            return false;
        }

        var desired = content.DesiredSize;
        var padding = window.Padding;
        var border = window.BorderThickness;
        var width = window.SizeToContent.HasFlag(SizeToContent.Width)
            ? Math.Clamp(desired.Width + padding.Left + padding.Right + border.Left + border.Right, window.MinWidth, window.MaxWidth)
            : window.ClientSize.Width;
        var height = window.SizeToContent.HasFlag(SizeToContent.Height)
            ? Math.Clamp(desired.Height + padding.Top + padding.Bottom + border.Top + border.Bottom, window.MinHeight, window.MaxHeight)
            : window.ClientSize.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return false;
        }

        size = new(width, height);
        var dpi = ReadDpi();
        return Math.Abs(WindowsChromeGeometry.ToPixels(width, dpi) - WindowsChromeGeometry.ToPixels(window.ClientSize.Width, dpi)) > 1 ||
               Math.Abs(WindowsChromeGeometry.ToPixels(height, dpi) - WindowsChromeGeometry.ToPixels(window.ClientSize.Height, dpi)) > 1;
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty || e.Property == Window.CanResizeProperty ||
            e.Property == Window.CanMinimizeProperty || e.Property == Window.CanMaximizeProperty ||
            e.Property == TopLevel.ActualThemeVariantProperty)
        {
            QueueRefresh(true);
        }
        else if (e.Property == Window.SizeToContentProperty)
        {
            QueueRefresh(false);
        }
    }

    private void OnTitleBarPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Layoutable.HeightProperty)
        {
            QueueRefresh(true);
        }
    }

    private static void CheckWin32(int result)
    {
        if (result == 0)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }
    }
}
