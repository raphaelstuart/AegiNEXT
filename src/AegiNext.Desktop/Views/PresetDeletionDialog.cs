using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class PresetDeletionDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal PresetDeletionDialog(PresetDeletionRequest request)
    {
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe("Settings.Delete").ToBinding()));
        var message = new TextBlock { Name = "PresetDeletionMessage", TextWrapping = TextWrapping.Wrap };
        localizationBindings.Add(message.Bind(TextBlock.TextProperty, Localization.Observe(() => request.IsDraftOnly
            ? Localization.Get("Settings.DiscardPresetDraftConfirmation")
            : Localization.Format("Settings.DeletePresetsConfirmation", request.Names.Length)).ToBinding()));
        var names = new TextBlock { Name = "PresetDeletionNames", Text = string.Join(Environment.NewLine, request.Names),
            TextWrapping = TextWrapping.Wrap };
        var cancel = new Button { Name = "CancelButton", IsCancel = true, IsDefault = true };
        var delete = new Button { Name = "DeleteButton" };
        localizationBindings.Add(cancel.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench.Cancel").ToBinding()));
        localizationBindings.Add(delete.Bind(ContentControl.ContentProperty, Localization.Observe("Settings.Delete").ToBinding()));
        cancel.Click += (_, _) => Close(false);
        delete.Click += (_, _) => Close(true);
        Opened += (_, _) => cancel.Focus();
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 16,
            Children =
            {
                message,
                new ScrollViewer { MaxHeight = 200, Content = names },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, delete }
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
