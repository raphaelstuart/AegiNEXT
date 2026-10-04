namespace AegiNext.Desktop.Layouts;

internal static class WorkspaceLayoutValidator
{
    internal static void Validate(WorkspaceLayoutSnapshot layout)
    {
        Validate(layout, WorkspaceLayoutSnapshot.CURRENT_VERSION, WorkbenchPanelIds.All);
    }

    internal static void ValidateLegacy(WorkspaceLayoutSnapshot layout, IReadOnlyList<string> panelIds)
    {
        Validate(layout, 1, panelIds);
    }

    private static void Validate(WorkspaceLayoutSnapshot layout, int version, IReadOnlyList<string> panelIds)
    {
        if (layout.Version != version)
        {
            throw new InvalidDataException($"Unsupported layout version: {layout.Version}.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        ValidateNode(layout.Main, ids, panelIds, 0);
        foreach (var floating in layout.Floating)
        {
            if (!double.IsFinite(floating.X) || !double.IsFinite(floating.Y)
                || !double.IsFinite(floating.Width) || floating.Width < 160 || floating.Width > 32000
                || !double.IsFinite(floating.Height) || floating.Height < 100 || floating.Height > 32000
                || !double.IsFinite(floating.Scaling) || floating.Scaling <= 0 || floating.Scaling > 16)
            {
                throw new InvalidDataException("Invalid floating window bounds.");
            }

            ValidateNode(floating.Content, ids, panelIds, 0);
        }

        foreach (var id in layout.HiddenPanelIds)
        {
            ValidatePanel(id, ids, panelIds);
        }

        if (!ids.SetEquals(panelIds))
        {
            throw new InvalidDataException("The layout must account for every workspace panel exactly once.");
        }

        if (layout.FocusedPanelId is { } focused && !panelIds.Contains(focused))
        {
            throw new InvalidDataException("Unknown focused panel.");
        }
    }

    private static void ValidateNode(LayoutNodeSnapshot node, HashSet<string> ids, IReadOnlyList<string> panelIds, int depth)
    {
        if (depth > 32 || !double.IsFinite(node.Proportion) || node.Proportion <= 0 || node.Proportion > 1000)
        {
            throw new InvalidDataException("Invalid layout depth or proportion.");
        }

        switch (node.Kind)
        {
            case "panel":
                ValidatePanel(node.PanelId, ids, panelIds);
                if (node.Children.Count != 0)
                {
                    throw new InvalidDataException("Panels cannot own child nodes.");
                }
                break;
            case "tabs":
                if (node.Children.Any(child => child.Kind != "panel"))
                {
                    throw new InvalidDataException("Tab groups can only contain panels.");
                }

                if (node.ActivePanelId is { } active && !node.Children.Any(child => child.PanelId == active))
                {
                    throw new InvalidDataException("The active tab is missing from its group.");
                }
                goto case "split";
            case "split":
                if (node.Orientation is not ("horizontal" or "vertical"))
                {
                    throw new InvalidDataException("Invalid split orientation.");
                }

                foreach (var child in node.Children)
                {
                    ValidateNode(child, ids, panelIds, depth + 1);
                }
                break;
            default:
                throw new InvalidDataException($"Unknown layout node kind: {node.Kind}.");
        }
    }

    private static void ValidatePanel(string? id, HashSet<string> ids, IReadOnlyList<string> panelIds)
    {
        if (id is null || !panelIds.Contains(id) || !ids.Add(id))
        {
            throw new InvalidDataException($"Unknown or duplicated panel: {id}.");
        }
    }
}
