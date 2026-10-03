namespace AegiNext.Desktop.Diagnostics;

internal sealed record WorkspaceDockProbeSample(string Action, bool HostAttached, bool HostEffectivelyVisible,
    string HostBounds, string? LayoutType, string? ActiveDockType, string? HostDataContextType,
    int RootControlCount, int ToolDockControlCount, IReadOnlyList<string> ParentChain,
    IReadOnlyList<string> VisualTree, IReadOnlyList<string> Panels);
