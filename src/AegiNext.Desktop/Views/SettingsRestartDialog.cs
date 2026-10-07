using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class SettingsRestartDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal SettingsRestartDialog()
    {
        Width = 460;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Classes.Add("business-surface");
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe("Settings.TransferRestartTitle").ToBinding()));
        var message = new TextBlock { Name = "SettingsRestartMessage", TextWrapping = TextWrapping.Wrap };
        var later = new Button { Name = "SettingsRestartLaterButton", IsCancel = true };
        var close = new Button { Name = "SettingsRestartConfirmButton", IsDefault = true };
        localizationBindings.Add(message.Bind(TextBlock.TextProperty, Localization.Observe("Settings.TransferRestartMessage").ToBinding()));
        localizationBindings.Add(later.Bind(ContentControl.ContentProperty, Localization.Observe("Settings.TransferLater").ToBinding()));
        localizationBindings.Add(close.Bind(ContentControl.ContentProperty, Localization.Observe("Settings.TransferCloseNow").ToBinding()));
        later.Click += (_, _) => Close(false);
        close.Click += (_, _) => Close(true);
        Opened += (_, _) => close.Focus();
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 20,
            Children =
            {
                message,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { later, close }
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
