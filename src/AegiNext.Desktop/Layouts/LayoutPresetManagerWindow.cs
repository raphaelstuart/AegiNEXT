using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutPresetManagerWindow : Window, IDisposable
{
    private readonly List<IDisposable> localizationBindings = [];
    private readonly LayoutPresetManagerViewModel viewModel;
    private bool disposed;

    internal LayoutPresetManagerWindow(WorkbenchLayoutController controller)
    {
        viewModel = new(controller);
        DataContext = viewModel;
        localizationBindings.Add(this.Bind(TitleProperty, ObserveTitle().ToBinding()));
        Width = 540;
        Height = 430;
        MinWidth = 420;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var list = new ListBox { ItemsSource = viewModel.Presets, MinHeight = 120 };
        list.Bind(ListBox.SelectedItemProperty, new Binding(nameof(viewModel.Selected)) { Mode = BindingMode.TwoWay });
        var name = new TextBox();
        localizationBindings.Add(name.Bind(TextBox.PlaceholderTextProperty, Localization.Observe("Layout.Name").ToBinding()));
        name.Bind(TextBox.TextProperty, new Binding(nameof(viewModel.Name)) { Mode = BindingMode.TwoWay });
        var error = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        error.Bind(TextBlock.TextProperty, new Binding(nameof(viewModel.Error)));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var apply = new Button { Command = viewModel.ApplyCommand };
        localizationBindings.Add(apply.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Apply").ToBinding()));
        var rename = new Button { Command = viewModel.RenameCommand };
        localizationBindings.Add(rename.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Rename").ToBinding()));
        var delete = new Button { Command = viewModel.DeleteCommand };
        localizationBindings.Add(delete.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Delete").ToBinding()));
        buttons.Children.Add(apply);
        buttons.Children.Add(rename);
        buttons.Children.Add(delete);
        var saveAs = new Button { Command = viewModel.SaveAsCommand };
        localizationBindings.Add(saveAs.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.SaveAs").ToBinding()));
        var close = new Button { HorizontalAlignment = HorizontalAlignment.Right };
        localizationBindings.Add(close.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Close").ToBinding()));
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
        Closed += OnClosed;
    }

    private static IObservable<string> ObserveTitle()
    {
        return Localization.Observe(() => Localization.Get("Layout.Manage").TrimEnd('…'));
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        Dispose();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        Closed -= OnClosed;
        foreach (var binding in localizationBindings)
        {
            binding.Dispose();
        }

        localizationBindings.Clear();
        viewModel.Dispose();
    }
}
