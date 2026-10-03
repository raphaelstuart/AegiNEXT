using AegiNext.Desktop.Localization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AegiNext.Desktop.Views;

internal sealed class UnsavedProjectDialog : Window
{
    internal UnsavedProjectDialog()
    {
        Title = WorkbenchText.Get("UnsavedTitle");
        Width = 410;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (key, result) in new[] { ("Cancel", 0), ("Discard", 2), ("Save", 1) })
        {
            var button = new Button { Content = WorkbenchText.Get(key), Padding = new Thickness(14, 7) };
            button.Click += (_, _) => Close(result);
            buttons.Children.Add(button);
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 24,
            Children = { new TextBlock { Text = WorkbenchText.Get("UnsavedText") }, buttons }
        };
    }
}
