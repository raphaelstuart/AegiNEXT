using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace AegiNext.Desktop.Windowing;

[SupportedOSPlatform("macos")]
internal static partial class MacOsCaptionButtons
{
    private const string OBJC_LIBRARY = "/usr/lib/libobjc.A.dylib";
    private static readonly nint standardButtonSelector = RegisterSelector("standardWindowButton:");
    private static readonly nint frameSelector = RegisterSelector("frame");
    private static readonly nint hiddenSelector = RegisterSelector("isHidden");

    internal static double MeasureLeftInset(nint window)
    {
        var right = 0d;
        for (var i = 0; i < 3; i++)
        {
            var button = GetButton(window, standardButtonSelector, i);
            if (button == 0 || IsHidden(button, hiddenSelector) != 0)
            {
                continue;
            }

            var frame = GetFrame(button);
            if (double.IsFinite(frame.X) && double.IsFinite(frame.Width) && frame.Width > 0)
            {
                right = Math.Max(right, frame.X + frame.Width);
            }
        }

        return right;
    }

    internal static int CountVisible(nint window)
    {
        var count = 0;
        for (var i = 0; i < 3; i++)
        {
            var button = GetButton(window, standardButtonSelector, i);
            if (button != 0 && IsHidden(button, hiddenSelector) == 0)
            {
                count++;
            }
        }

        return count;
    }

    private static MacOsRect GetFrame(nint button)
    {
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            GetFrameIntel(out var frame, button, frameSelector);
            return frame;
        }

        return GetFrameArm(button, frameSelector);
    }

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterSelector(string name);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial nint GetButton(nint receiver, nint selector, nint button);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial byte IsHidden(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial MacOsRect GetFrameArm(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend_stret")]
    private static partial void GetFrameIntel(out MacOsRect frame, nint receiver, nint selector);
}
