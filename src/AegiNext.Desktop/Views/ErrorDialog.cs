using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class ErrorDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal ErrorDialog(string titleKey, string message)
    {
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("business-surface");
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe(titleKey).ToBinding()));
        var details = new TextBox
        {
            Name = "ErrorDetails", Text = message, IsReadOnly = true, AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap, MaxHeight = 240
        };
        var logged = new TextBlock { Name = "ErrorLoggedMessage", TextWrapping = TextWrapping.Wrap };
        localizationBindings.Add(logged.Bind(TextBlock.TextProperty, Localization.Observe("Workbench.ErrorDetailsLogged").ToBinding()));
        var confirm = new Button { Name = "ErrorConfirmButton", IsDefault = true, IsCancel = true };
        localizationBindings.Add(confirm.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Confirm").ToBinding()));
        confirm.Click += (_, _) => Close();
        Opened += (_, _) => confirm.Focus();
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children =
            {
                details, logged,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Children = { confirm }
                }
            }
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
