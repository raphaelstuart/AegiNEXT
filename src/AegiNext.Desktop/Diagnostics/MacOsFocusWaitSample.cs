namespace AegiNext.Desktop.Diagnostics;

internal sealed record MacOsFocusWaitSample(string Action, string FocusedHost, bool WindowMenuMode,
    long RequestedKeyWindowHandle, long ObservedKeyWindowHandle, long ElapsedMilliseconds, int PollCount,
    bool Matched);
