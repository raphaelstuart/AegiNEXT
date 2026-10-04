using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;

namespace AegiNext.Desktop.Diagnostics;

[SupportedOSPlatform("macos")]
internal static partial class MacOsActualMenuSnapshot
{
    private const string OBJC_LIBRARY = "/usr/lib/libobjc.A.dylib";
    private static readonly nint sharedApplicationSelector = RegisterSelector("sharedApplication");
    private static readonly nint mainMenuSelector = RegisterSelector("mainMenu");
    private static readonly nint keyWindowSelector = RegisterSelector("keyWindow");
    private static readonly nint titleSelector = RegisterSelector("title");
    private static readonly nint submenuSelector = RegisterSelector("submenu");
    private static readonly nint countSelector = RegisterSelector("numberOfItems");
    private static readonly nint itemSelector = RegisterSelector("itemAtIndex:");
    private static readonly nint utf8Selector = RegisterSelector("UTF8String");

    internal static MacOsActualMenuSample Capture(string action, string focusedHost, bool windowMenuMode,
        bool managedRootsPreserved)
    {
        Dispatcher.UIThread.VerifyAccess();
        var application = GetObject(GetClass("NSApplication"), sharedApplicationSelector);
        var mainMenu = GetObject(application, mainMenuSelector);
        var keyWindow = GetObject(application, keyWindowSelector);
        var count = checked((int)GetCount(mainMenu, countSelector));
        var items = new MacOsActualMenuItem[count];
        for (var index = 0; index < count; index++)
        {
            var item = GetItem(mainMenu, itemSelector, (nuint)index);
            var submenu = GetObject(item, submenuSelector);
            var childCount = checked((int)GetCount(submenu, countSelector));
            var children = new string[childCount];
            for (var childIndex = 0; childIndex < childCount; childIndex++)
            {
                children[childIndex] = ReadTitle(GetItem(submenu, itemSelector, (nuint)childIndex));
            }

            items[index] = new(ReadTitle(item), children);
        }

        return new(action, focusedHost, windowMenuMode, ReadTitle(keyWindow), (long)mainMenu, items,
            managedRootsPreserved, (long)keyWindow);
    }

    internal static nint ReadKeyWindowHandle()
    {
        Dispatcher.UIThread.VerifyAccess();
        var application = GetObject(GetClass("NSApplication"), sharedApplicationSelector);
        return GetObject(application, keyWindowSelector);
    }

    private static string ReadTitle(nint receiver)
    {
        var title = GetObject(receiver, titleSelector);
        var characters = GetObject(title, utf8Selector);
        return characters == 0 ? string.Empty : Marshal.PtrToStringUTF8(characters) ?? string.Empty;
    }

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_getClass", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetClass(string name);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "sel_registerName", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint RegisterSelector(string name);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial nint GetObject(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial nuint GetCount(nint receiver, nint selector);

    [LibraryImport(OBJC_LIBRARY, EntryPoint = "objc_msgSend")]
    private static partial nint GetItem(nint receiver, nint selector, nuint index);
}
