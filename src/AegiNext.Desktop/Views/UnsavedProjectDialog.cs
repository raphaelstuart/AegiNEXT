using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AegiNext.Desktop.Views;

internal sealed class UnsavedProjectDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal UnsavedProjectDialog()
    {
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe("Workbench.UnsavedTitle").ToBinding()));
        Width = 410;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (key, result) in new[] { ("Cancel", 0), ("Discard", 2), ("Save", 1) })
        {
            var button = new Button { Name = key + "Button", Padding = new Thickness(14, 7) };
            localizationBindings.Add(button.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench." + key).ToBinding()));
            button.Click += (_, _) => Close(result);
            buttons.Children.Add(button);
        }

        var message = new TextBlock { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        localizationBindings.Add(message.Bind(TextBlock.TextProperty, Localization.Observe("Workbench.UnsavedText").ToBinding()));
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 24,
            Children = { message, buttons }
        };
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        foreach (var binding in localizationBindings)
        {
            binding.Dispose();
        }

        localizationBindings.Clear();
    }
}
