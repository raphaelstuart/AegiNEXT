using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class SubtitleConversionDialog : Window
{
    private readonly List<IDisposable> bindings = [];

    internal SubtitleConversionDialog(IReadOnlyList<string> diagnostics)
    {
        Width = 640;
        Height = 400;
        MinWidth = 380;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        bindings.Add(this.Bind(TitleProperty, Localization.Observe("Workbench.SubtitleConversionTitle").ToBinding()));
        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        bindings.Add(message.Bind(TextBlock.TextProperty, Localization.Observe("Workbench.SubtitleConversionText").ToBinding()));
        var cancel = new Button { IsCancel = true };
        var proceed = new Button { IsDefault = true };
        bindings.Add(cancel.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Cancel").ToBinding()));
        bindings.Add(proceed.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.ContinueConversion").ToBinding()));
        cancel.Click += (_, _) => Close(false);
        proceed.Click += (_, _) => Close(true);
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,*,Auto"), RowSpacing = 16 };
        root.Children.Add(message);
        var detail = new TextBox { Text = string.Join(Environment.NewLine, diagnostics), IsReadOnly = true,
            AcceptsReturn = true, TextWrapping = TextWrapping.Wrap };
        Grid.SetRow(detail, 1);
        root.Children.Add(detail);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, proceed } };
        Grid.SetRow(buttons, 2);
        root.Children.Add(buttons);
        Content = root;
        Closed += (_, _) =>
        {
            foreach (var binding in bindings)
            {
                binding.Dispose();
            }
            bindings.Clear();
        };
    }
}
