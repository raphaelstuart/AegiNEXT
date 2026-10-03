namespace AegiNext.Desktop.Layouts;

internal sealed record WorkspaceLayoutPreset(string Id, string Name, bool IsReadOnly, WorkspaceLayoutSnapshot Layout);
