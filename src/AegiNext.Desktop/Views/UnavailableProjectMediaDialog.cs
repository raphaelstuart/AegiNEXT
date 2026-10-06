using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class UnavailableProjectMediaDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal UnavailableProjectMediaDialog(string mediaPath, string reason)
    {
        Width = 540;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe("Workbench.UnavailableMediaTitle").ToBinding()));
        var message = new TextBlock { Name = "UnavailableMediaMessage", TextWrapping = TextWrapping.Wrap };
        localizationBindings.Add(message.Bind(TextBlock.TextProperty, Localization.Observe(() => Localization.Format(
            "Workbench.UnavailableMediaText", Localization.Get("Workbench.File"), Localization.Get("Workbench.Open"))).ToBinding()));
        var details = new TextBox
        {
            Name = "UnavailableMediaDetails", Text = mediaPath + Environment.NewLine + reason,
            IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            MaxHeight = 180
        };
        var cancel = new Button { Name = "CancelButton", IsCancel = true, IsDefault = true };
        var proceed = new Button { Name = "ContinueButton" };
        localizationBindings.Add(cancel.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Cancel").ToBinding()));
        localizationBindings.Add(proceed.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.ContinueOpening").ToBinding()));
        cancel.Click += (_, _) => Close(false);
        proceed.Click += (_, _) => Close(true);
        Opened += (_, _) => cancel.Focus();
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8, Children = { cancel, proceed }
        };
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16, Children = { message, details, buttons }
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
