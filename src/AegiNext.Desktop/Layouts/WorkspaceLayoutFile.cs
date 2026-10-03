namespace AegiNext.Desktop.Layouts;

internal sealed record WorkspaceLayoutFile
{
    public int Version { get; init; } = WorkspaceLayoutSnapshot.CURRENT_VERSION;
    public WorkspaceLayoutSnapshot Current { get; init; } = WorkspaceLayoutPresets.Standard;
    public string CurrentPresetId { get; init; } = WorkspaceLayoutPresets.STANDARD;
    public IReadOnlyList<WorkspaceLayoutPreset> Presets { get; init; } = [];
}
