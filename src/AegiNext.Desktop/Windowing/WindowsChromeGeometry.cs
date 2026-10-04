using Avalonia;

namespace AegiNext.Desktop.Windowing;

internal static class WindowsChromeGeometry
{
    internal const double STANDARD_DPI = 96;
    private const uint NON_INTERACTIVE_CAPTION_STATE = 0x00008000 | 0x00010000 | 0x00000001;

    internal static int ToPixels(double value, uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        if (!double.IsFinite(value) || value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value));
        }

        var scaled = value * dpi / STANDARD_DPI;
        var nearest = Math.Round(scaled);
        if (nearest > 0 && scaled >= Math.BitDecrement(Math.BitDecrement(nearest)) &&
            scaled <= Math.BitIncrement(Math.BitIncrement(nearest)))
        {
            return checked((int)nearest);
        }

        return checked((int)Math.Ceiling(scaled));
    }

    internal static PixelSize GetOuterSize(Size clientSize, uint dpi, WindowsFrameInsets frame)
    {
        if (clientSize.Width <= 0 || clientSize.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(clientSize));
        }

        return new(
            checked(ToPixels(clientSize.Width, dpi) + frame.Left + frame.Right),
            checked(ToPixels(clientSize.Height, dpi) + frame.Bottom));
    }

    internal static WindowsRect GetClientRect(WindowsRect outer, WindowsFrameInsets frame, bool maximized)
    {
        return new()
        {
            Left = checked(outer.Left + frame.Left),
            Top = checked(outer.Top + (maximized ? frame.Top : 0)),
            Right = checked(outer.Right - frame.Right),
            Bottom = checked(outer.Bottom - frame.Bottom)
        };
    }

    internal static WindowsPoint GetScreenPoint(nint coordinates)
    {
        return new()
        {
            X = unchecked((short)((long)coordinates & 0xFFFF)),
            Y = unchecked((short)(((long)coordinates >> 16) & 0xFFFF))
        };
    }

    internal static WindowsChromeHitTest HitResizeFrame(WindowsPoint point, WindowsRect outer,
        WindowsFrameInsets frame, bool canResize, bool maximized)
    {
        if (!canResize || maximized || point.X < outer.Left || point.X >= outer.Right ||
            point.Y < outer.Top || point.Y >= outer.Bottom)
        {
            return WindowsChromeHitTest.NONE;
        }

        var left = point.X < outer.Left + frame.Left;
        var right = point.X >= outer.Right - frame.Right;
        var top = point.Y < outer.Top + frame.Top;
        var bottom = point.Y >= outer.Bottom - frame.Bottom;
        if (top)
        {
            return left ? WindowsChromeHitTest.TOP_LEFT : right ? WindowsChromeHitTest.TOP_RIGHT :
                WindowsChromeHitTest.TOP;
        }

        if (bottom)
        {
            return left ? WindowsChromeHitTest.BOTTOM_LEFT : right ? WindowsChromeHitTest.BOTTOM_RIGHT :
                WindowsChromeHitTest.BOTTOM;
        }

        return left ? WindowsChromeHitTest.LEFT : right ? WindowsChromeHitTest.RIGHT : WindowsChromeHitTest.NONE;
    }

    internal static Thickness GetCaptionInsets(WindowsRect windowRect, WindowsPoint clientOrigin,
        int clientWidth, WindowsRect captionBounds, uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        var captionLeft = (long)windowRect.Left + captionBounds.Left - clientOrigin.X;
        var right = Math.Clamp(clientWidth - captionLeft, 0, clientWidth);
        return new(0, 0, right * STANDARD_DPI / dpi, 0);
    }

    internal static WindowsChromeHitTest HitCaptionButtons(WindowsPoint screenPoint, in WindowsTitleBarInfoEx titleBar)
    {
        if (HitCaptionButton(screenPoint, titleBar.MinimizeBounds, titleBar.MinimizeState))
        {
            return WindowsChromeHitTest.MINIMIZE;
        }

        if (HitCaptionButton(screenPoint, titleBar.MaximizeBounds, titleBar.MaximizeState))
        {
            return WindowsChromeHitTest.MAXIMIZE;
        }

        return HitCaptionButton(screenPoint, titleBar.CloseBounds, titleBar.CloseState)
            ? WindowsChromeHitTest.CLOSE : WindowsChromeHitTest.NONE;
    }

    internal static Rect GetCaptionAperture(WindowsRect windowRect, WindowsPoint clientOrigin,
        Size clientSize, WindowsRect captionBounds, uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        var left = ((long)windowRect.Left + captionBounds.Left - clientOrigin.X) * STANDARD_DPI / dpi;
        var bottom = ((long)windowRect.Top + captionBounds.Bottom - clientOrigin.Y) * STANDARD_DPI / dpi;
        left = Math.Clamp(left, 0, clientSize.Width);
        bottom = Math.Clamp(bottom, 0, clientSize.Height);
        return new(left, 0, clientSize.Width - left, bottom);
    }

    internal static bool IsUsableCaptionBounds(WindowsRect bounds)
    {
        return bounds.Left >= 0 && bounds.Top >= 0 && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;
    }

    private static bool HitCaptionButton(WindowsPoint screenPoint, WindowsRect bounds, uint state)
    {
        return (state & NON_INTERACTIVE_CAPTION_STATE) == 0 && screenPoint.X >= bounds.Left &&
               screenPoint.X < bounds.Right && screenPoint.Y >= bounds.Top && screenPoint.Y < bounds.Bottom;
    }
}
