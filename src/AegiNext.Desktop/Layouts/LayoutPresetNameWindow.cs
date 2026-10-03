using Avalonia.Controls;
using Avalonia.Layout;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutPresetNameWindow : Window
{
    internal LayoutPresetNameWindow(WorkbenchLayoutController controller)
    {
        var culture = controller.Culture;
        Title = LayoutText.Get("SaveAs", culture).TrimEnd('…');
        Width = 380;
        Height = 180;
        MinWidth = 300;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var name = new TextBox { PlaceholderText = LayoutText.Get("Name", culture), MaxLength = 80 };
        var save = new Button { Content = LayoutText.Get("Save", culture), IsDefault = true, IsEnabled = false };
        name.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        save.Click += (_, _) => Close(name.Text?.Trim());
        var cancel = new Button { Content = LayoutText.Get("Cancel", culture), IsCancel = true };
        cancel.Click += (_, _) => Close(null);
        var label = new TextBlock { Text = LayoutText.Get("Name", culture) };
        Content = new StackPanel
        {
            Margin = new(16), Spacing = 14,
            Children =
            {
                label,
                name,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, save } }
            }
        };
        void RefreshLabels(object? sender, EventArgs args)
        {
            Title = LayoutText.Get("SaveAs", controller.Culture).TrimEnd('…');
            label.Text = LayoutText.Get("Name", controller.Culture);
            name.PlaceholderText = label.Text;
            save.Content = LayoutText.Get("Save", controller.Culture);
            cancel.Content = LayoutText.Get("Cancel", controller.Culture);
        }
        controller.Changed += RefreshLabels;
        Closed += (_, _) => controller.Changed -= RefreshLabels;
        Opened += (_, _) => name.Focus();
    }
}
