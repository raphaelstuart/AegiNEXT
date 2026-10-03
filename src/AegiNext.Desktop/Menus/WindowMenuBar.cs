using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Styling;
using Avalonia.Controls;
using Avalonia;

namespace AegiNext.Desktop.Menus;

internal sealed class WindowMenuBar : UserControl, IDisposable
{
    private readonly WorkbenchMenuCatalog catalog;
    private readonly Menu fullMenu = new() { Name = "MainMenu" };
    private readonly Menu overflowMenu = new();
    private double availableWidth = double.PositiveInfinity;

    internal WindowMenuBar(WorkbenchMenuCatalog catalog)
    {
        this.catalog = catalog;
        Content = fullMenu;
        catalog.Changed += OnChanged;
        AttachedToVisualTree += OnAttached;
        Refresh();
    }

    internal void SetAvailableWidth(double width)
    {
        availableWidth = Math.Max(0, width);
        RefreshOverflow();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        catalog.Changed -= OnChanged;
        AttachedToVisualTree -= OnAttached;
    }

    private void OnChanged(object? sender, EventArgs e)
    {
        Refresh();
    }

    private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        fullMenu.Measure(new(double.PositiveInfinity, 40));
        RefreshOverflow();
    }

    private void Refresh()
    {
        fullMenu.Items.Clear();
        overflowMenu.Items.Clear();
        var overflow = new MenuItem { Header = "☰" };
        ToolTip.SetTip(overflow, WorkbenchText.Get("View"));
        foreach (var group in WorkbenchMenuCatalog.Groups)
        {
            fullMenu.Items.Add(CreateGroup(group));
            overflow.Items.Add(CreateGroup(group));
        }

        overflowMenu.Items.Add(overflow);
        fullMenu.Measure(new(double.PositiveInfinity, 40));
        RefreshOverflow();
    }

    private MenuItem CreateGroup(WorkbenchMenuGroup group)
    {
        var label = WorkbenchText.Get(group.Key);
        if (group.Key == "Layouts" && catalog.IsLayoutModified)
        {
            label += $" ({WorkbenchText.Get("LayoutModified")})";
        }

        var item = new MenuItem { Header = label };
        if (group.Key == "Layouts")
        {
            foreach (var choice in catalog.LayoutChoices)
            {
                item.Items.Add(new MenuItem
                {
                    Header = choice.Title, Command = choice.Command,
                    ToggleType = MenuItemToggleType.Radio, IsChecked = choice.IsSelected
                });
            }

            item.Items.Add(new Separator());
        }

        foreach (var command in group.Commands)
        {
            item.Items.Add(command is { } id
                ? new MenuItem
                {
                    Header = WorkbenchMenuCatalog.GetLabel(id), Command = catalog.GetCommand(id),
                    InputGesture = catalog.GetGesture(id), Icon = WorkbenchIcon.Create(id.ToString())
                }
                : new Separator());
        }

        return item;
    }

    private void RefreshOverflow()
    {
        var next = fullMenu.DesiredSize.Width > availableWidth ? overflowMenu : fullMenu;
        if (!ReferenceEquals(Content, next))
        {
            Content = next;
        }
    }
}
