using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutPresetNameWindow : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal LayoutPresetNameWindow()
    {
        localizationBindings.Add(this.Bind(TitleProperty, ObserveTitle().ToBinding()));
        Width = 380;
        SizeToContent = SizeToContent.Height;
        MinWidth = 300;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var name = new TextBox { MaxLength = 80 };
        localizationBindings.Add(name.Bind(TextBox.PlaceholderTextProperty, Localization.Observe("Layout.Name").ToBinding()));
        var save = new Button { IsDefault = true, IsEnabled = false };
        localizationBindings.Add(save.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Save").ToBinding()));
        name.TextChanged += (_, _) => save.IsEnabled = !string.IsNullOrWhiteSpace(name.Text);
        save.Click += (_, _) => Close(name.Text?.Trim());
        var cancel = new Button { IsCancel = true };
        localizationBindings.Add(cancel.Bind(ContentControl.ContentProperty, Localization.Observe("Layout.Cancel").ToBinding()));
        cancel.Click += (_, _) => Close(null);
        var label = new TextBlock();
        localizationBindings.Add(label.Bind(TextBlock.TextProperty, Localization.Observe("Layout.Name").ToBinding()));
        Content = new StackPanel
        {
            Margin = new(16), Spacing = 14,
            Children =
            {
                label,
                name,
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Children = { cancel, save } }
            }
        };
        Closed += OnClosed;
        Opened += (_, _) => name.Focus();
    }

    private static IObservable<string> ObserveTitle()
    {
        return Localization.Observe(() => Localization.Get("Layout.SaveAs").TrimEnd('…'));
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
