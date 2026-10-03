using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutPresetManagerWindow : Window
{
    internal LayoutPresetManagerWindow(WorkbenchLayoutController controller, CultureInfo culture)
    {
        var viewModel = new LayoutPresetManagerViewModel(controller);
        DataContext = viewModel;
        Title = LayoutText.Get("Manage", culture).TrimEnd('…');
        Width = 540;
        Height = 430;
        MinWidth = 420;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox { ItemsSource = viewModel.Presets, MinHeight = 120 };
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(viewModel.Selected)) { Mode = BindingMode.TwoWay });
        var name = new TextBox { PlaceholderText = LayoutText.Get("Name", culture) };
        name.Bind(TextBox.TextProperty, new Binding(nameof(viewModel.Name)) { Mode = BindingMode.TwoWay });
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        error.Bind(TextBlock.TextProperty, new Binding(nameof(viewModel.Error)));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var apply = new Button { Content = LayoutText.Get("Apply", culture), Command = viewModel.ApplyCommand };
        var rename = new Button { Content = LayoutText.Get("Rename", culture), Command = viewModel.RenameCommand };
        var delete = new Button { Content = LayoutText.Get("Delete", culture), Command = viewModel.DeleteCommand };
        buttons.Children.Add(apply);
        buttons.Children.Add(rename);
        buttons.Children.Add(delete);
        var saveAs = new Button { Content = LayoutText.Get("SaveAs", culture), Command = viewModel.SaveAsCommand };
        var close = new Button { Content = LayoutText.Get("Close", culture), HorizontalAlignment = HorizontalAlignment.Right };
        close.Click += (_, _) => Close();
        var body = new Grid { Margin = new(16), RowDefinitions = new("*,Auto,Auto,Auto,Auto,Auto"), RowSpacing = 12 };
        body.Children.Add(list);
        Grid.SetRow(name, 1);
        body.Children.Add(name);
        Grid.SetRow(buttons, 2);
        body.Children.Add(buttons);
        Grid.SetRow(saveAs, 3);
        body.Children.Add(saveAs);
        Grid.SetRow(error, 4);
        body.Children.Add(error);
        Grid.SetRow(close, 5);
        body.Children.Add(close);
        Content = body;
        void RefreshLabels(object? sender, EventArgs args)
        {
            Title = LayoutText.Get("Manage", controller.Culture).TrimEnd('…');
            name.PlaceholderText = LayoutText.Get("Name", controller.Culture);
            apply.Content = LayoutText.Get("Apply", controller.Culture);
            rename.Content = LayoutText.Get("Rename", controller.Culture);
            delete.Content = LayoutText.Get("Delete", controller.Culture);
            saveAs.Content = LayoutText.Get("SaveAs", controller.Culture);
            close.Content = LayoutText.Get("Close", controller.Culture);
        }
        controller.Changed += RefreshLabels;
        Closed += (_, _) =>
        {
            controller.Changed -= RefreshLabels;
            viewModel.Dispose();
        };
    }
}
