using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Menus;
using Avalonia.Controls;
using Avalonia.Input;

namespace AegiNext.Desktop.Windowing;

internal sealed record WorkbenchWindowEntry(Window Window, WindowTitleBar TitleBar, IWindowChrome Chrome,
    WindowMenuBar MenuBar, WorkbenchNativeMenu NativeMenu, Func<string> TitleProvider, HashSet<Key> PressedKeys,
    WorkbenchWindowRole Role);
