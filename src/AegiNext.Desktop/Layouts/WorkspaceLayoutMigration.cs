namespace AegiNext.Desktop.Layouts;

internal static class WorkspaceLayoutMigration
{
    private static readonly string[] legacyPanelIds =
        [WorkbenchPanelIds.PREVIEW, WorkbenchPanelIds.TIMELINE, WorkbenchPanelIds.SUBTITLES,
            WorkbenchPanelIds.STYLES, WorkbenchPanelIds.EFFECTS, WorkbenchPanelIds.EXPORT];

    internal static WorkspaceLayoutFile Upgrade(WorkspaceLayoutFile file)
    {
        if (file.Version != 1)
        {
            return file;
        }

        var current = UpgradeSnapshot(file.Current);
        var presets = file.Presets.Select(preset => preset with { Layout = UpgradeSnapshot(preset.Layout) }).ToArray();
        var builtIn = WorkspaceLayoutPresets.BuiltIn.FirstOrDefault(preset => preset.Id == file.CurrentPresetId);
        if (builtIn is not null)
        {
            var legacy = builtIn.Layout with
            {
                Version = 1,
                Main = RemoveLog(builtIn.Layout.Main),
                HiddenPanelIds = builtIn.Layout.HiddenPanelIds.Where(id => id != WorkbenchPanelIds.LOG).ToArray()
            };
            if (WorkspaceLayoutStore.Fingerprint(file.Current) == WorkspaceLayoutStore.Fingerprint(legacy))
            {
                current = builtIn.Layout;
            }
        }

        return file with { Version = WorkspaceLayoutSnapshot.CURRENT_VERSION, Current = current, Presets = presets };
    }

    private static WorkspaceLayoutSnapshot UpgradeSnapshot(WorkspaceLayoutSnapshot layout)
    {
        WorkspaceLayoutValidator.ValidateLegacy(layout, legacyPanelIds);
        return layout with
        {
            Version = WorkspaceLayoutSnapshot.CURRENT_VERSION,
            HiddenPanelIds = layout.HiddenPanelIds.Append(WorkbenchPanelIds.LOG).ToArray()
        };
    }

    private static LayoutNodeSnapshot RemoveLog(LayoutNodeSnapshot node)
    {
        return node with
        {
            Children = node.Children.Where(child => child.PanelId != WorkbenchPanelIds.LOG).Select(RemoveLog).ToArray()
        };
    }
}
