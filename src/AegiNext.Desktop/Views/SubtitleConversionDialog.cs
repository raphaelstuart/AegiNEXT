using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class SubtitleConversionDialog : Window
{
    private readonly List<IDisposable> bindings = [];

    internal SubtitleConversionDialog(SubtitleConversionReview review)
    {
        Width = 640;
        Height = 400;
        MinWidth = 380;
        MinHeight = 260;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        bindings.Add(this.Bind(TitleProperty, Localization.Observe("Workbench.SubtitleConversionTitle").ToBinding()));
        var message = new TextBlock { Name = "ConversionMessage", TextWrapping = TextWrapping.Wrap };
        bindings.Add(message.Bind(TextBlock.TextProperty, Localization.Observe("Workbench.SubtitleConversionText").ToBinding()));
        var summary = new TextBlock { Name = "ConversionSummary", TextWrapping = TextWrapping.Wrap };
        bindings.Add(summary.Bind(TextBlock.TextProperty, Localization.Observe(review.FormatSummary).ToBinding()));
        var cancel = new Button { Name = "CancelButton", IsCancel = true };
        var proceed = new Button { Name = "ContinueButton", IsDefault = true };
        bindings.Add(cancel.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Cancel").ToBinding()));
        bindings.Add(proceed.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.ContinueConversion").ToBinding()));
        cancel.Click += (_, _) => Close(false);
        proceed.Click += (_, _) => Close(true);
        var root = new Grid { Margin = new Thickness(20), RowDefinitions = new("Auto,Auto,*,Auto"), RowSpacing = 12 };
        root.Children.Add(message);
        Grid.SetRow(summary, 1);
        root.Children.Add(summary);
        var detail = new TextBox
        {
            Name = "ConversionDetails", IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, VerticalContentAlignment = VerticalAlignment.Top,
            Classes = { "multiline-input" }
        };
        bindings.Add(detail.Bind(TextBox.TextProperty, Localization.Observe(review.FormatDetails).ToBinding()));
        ScrollViewer.SetVerticalScrollBarVisibility(detail, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        Grid.SetRow(detail, 2);
        root.Children.Add(detail);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, proceed } };
        Grid.SetRow(buttons, 3);
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
