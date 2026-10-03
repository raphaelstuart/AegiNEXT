using Dock.Model.Controls;
using Dock.Model.Core;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchDockSnapshotCodec
{
    private readonly WorkbenchDockFactory factory;
    private readonly IReadOnlyDictionary<string, WorkbenchDockPanel> panels;

    internal WorkbenchDockSnapshotCodec(WorkbenchDockFactory factory, IReadOnlyDictionary<string, WorkbenchDockPanel> panels)
    {
        this.factory = factory;
        this.panels = panels;
    }

    internal IRootDock BuildRoot(LayoutNodeSnapshot node)
    {
        var root = factory.CreateRootDock();
        root.Id = "workspace-root-" + Guid.NewGuid().ToString("N");
        root.IsCollapsable = false;
        root.VisibleDockables = factory.CreateList(Build(node));
        root.ActiveDockable = root.VisibleDockables[0];
        root.HiddenDockables = factory.CreateList<IDockable>();
        root.Windows = factory.CreateList<IDockWindow>();
        return root;
    }

    internal LayoutNodeSnapshot CaptureRoot(IRootDock root)
    {
        var children = root.VisibleDockables?.Where(dockable => dockable is not ISplitter).ToArray() ?? [];
        return children.Length == 1 ? Capture(children[0]) : new()
        {
            Kind = "split", Children = children.Select(Capture).ToArray()
        };
    }

    internal static IEnumerable<IDockable> Enumerate(IDockable dockable)
    {
        yield return dockable;
        if (dockable is IDock dock)
        {
            foreach (var child in dock.VisibleDockables ?? [])
            {
                foreach (var nested in Enumerate(child))
                {
                    yield return nested;
                }
            }
        }
    }

    private IDockable Build(LayoutNodeSnapshot node)
    {
        if (node.Kind == "panel")
        {
            return panels[node.PanelId!];
        }

        var children = node.Children.Select(Build).ToArray();
        IDock result;
        if (node.Kind == "tabs")
        {
            var tabs = factory.CreateToolDock();
            tabs.Id = "workspace-tabs-" + Guid.NewGuid().ToString("N");
            tabs.IsCollapsable = true;
            tabs.VisibleDockables = factory.CreateList(children);
            tabs.ActiveDockable = children.FirstOrDefault(child => child.Id == node.ActivePanelId) ?? children.FirstOrDefault();
            result = tabs;
        }
        else
        {
            var split = factory.CreateProportionalDock();
            split.Id = "workspace-split-" + Guid.NewGuid().ToString("N");
            split.Orientation = node.Orientation == "vertical" ? Orientation.Vertical : Orientation.Horizontal;
            split.VisibleDockables = factory.CreateList<IDockable>();
            foreach (var child in children)
            {
                if (split.VisibleDockables.Count > 0)
                {
                    var splitter = factory.CreateProportionalDockSplitter();
                    splitter.CanResize = true;
                    splitter.ResizePreview = true;
                    split.VisibleDockables.Add(splitter);
                }
                split.VisibleDockables.Add(child);
            }
            result = split;
        }

        result.Proportion = node.Proportion;
        return result;
    }

    private LayoutNodeSnapshot Capture(IDockable dockable)
    {
        var proportion = double.IsFinite(dockable.Proportion) && dockable.Proportion > 0 ? dockable.Proportion : 1;
        if (dockable is WorkbenchDockPanel panel)
        {
            return new() { Kind = "panel", PanelId = panel.Id };
        }

        if (dockable is IDock dock)
        {
            return new()
            {
                Kind = dock is IToolDock ? "tabs" : "split",
                Orientation = dock is IProportionalDock { Orientation: Orientation.Vertical } ? "vertical" : "horizontal",
                Proportion = proportion,
                ActivePanelId = dock.ActiveDockable is WorkbenchDockPanel active ? active.Id : null,
                Children = (dock.VisibleDockables ?? []).Where(child => child is not ISplitter).Select(Capture).ToArray()
            };
        }

        throw new InvalidDataException($"Unsupported workspace dock node: {dockable.GetType().Name}.");
    }
}
