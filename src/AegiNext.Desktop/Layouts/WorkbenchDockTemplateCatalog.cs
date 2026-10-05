using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Dock.Avalonia.Controls;
using Dock.Controls.ProportionalStackPanel;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Settings;

namespace AegiNext.Desktop.Layouts;

internal static class WorkbenchDockTemplateCatalog
{
    internal static void Install(DockControl host)
    {
        host.AutoCreateDataTemplates = false;
        host.DataTemplates.Add(new WorkbenchDockPanelTemplate());
        host.DataTemplates.Add(new FuncDataTemplate<IRootDock>(value => value is not null, (_, _) => new RootDockControl()));
        host.DataTemplates.Add(new FuncDataTemplate<IProportionalDock>(value => value is not null, (_, _) => new ProportionalDockControl()));
        host.DataTemplates.Add(new FuncDataTemplate<IToolDock>(value => value is not null, (_, _) => CreateToolDockControl()));
        host.DataTemplates.Add(new FuncDataTemplate<IProportionalDockSplitter>(value => value is not null, (_, _) => new ProportionalStackPanelSplitter
        {
            [!ProportionalStackPanelSplitter.IsResizingEnabledProperty] = new Binding(nameof(IProportionalDockSplitter.CanResize)),
            [!ProportionalStackPanelSplitter.PreviewResizeProperty] = new Binding(nameof(IProportionalDockSplitter.ResizePreview))
        }));
    }

    private static ToolDockControl CreateToolDockControl()
    {
        return new()
        {
            Template = new FuncControlTemplate<ToolDockControl>((_, scope) =>
            {
                var tracking = new DockableControl
                {
                    Name = "PART_DockableControl",
                    TrackingMode = TrackingMode.Visible,
                    [~Panel.BackgroundProperty] = new TemplateBinding(TemplatedControl.BackgroundProperty)
                }.RegisterInNameScope(scope);
                DockProperties.SetIsDropArea(tracking, true);
                DockProperties.SetIsDockTarget(tracking, true);
                var frame = new Border
                {
                    Name = "PART_WorkspaceFrame",
                    [!Avalonia.Visual.IsVisibleProperty] = new Binding("VisibleDockables.Count")
                    {
                        Converter = new FuncValueConverter<int, bool>(count => count > 0)
                    },
                    Child = new WorkbenchToolChromeControl
                    {
                        Content = new WorkbenchToolControl(),
                        [!ToolChromeControl.IsActiveProperty] = new Binding(nameof(IToolDock.IsActive))
                    }
                };
                frame.Classes.Add("workspace-frame");
                tracking.Children.Add(frame);
                return tracking;
            })
        };
    }
}
