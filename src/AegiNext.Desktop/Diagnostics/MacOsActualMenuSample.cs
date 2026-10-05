namespace AegiNext.Desktop.Diagnostics;

internal sealed record MacOsActualMenuSample(string Action, string FocusedHost, bool WindowMenuMode,
    string KeyWindowTitle, long MainMenuHandle, MacOsActualMenuItem[] Items, bool ManagedRootsPreserved,
    long KeyWindowHandle = 0, long RequestedKeyWindowHandle = 0, bool ApplicationIsActive = false,
    string FrontmostApplicationBundleId = "", string FrontmostApplicationName = "", int FrontmostApplicationProcessId = 0)
{
    public int ProbeProcessId { get; } = Environment.ProcessId;
}
