namespace AegiNext.Desktop.Diagnostics;

internal sealed record MacOsActualMenuSample(string Action, string FocusedHost, bool WindowMenuMode,
    string KeyWindowTitle, long MainMenuHandle, MacOsActualMenuItem[] Items, bool ManagedRootsPreserved,
    long KeyWindowHandle = 0, long RequestedKeyWindowHandle = 0);
