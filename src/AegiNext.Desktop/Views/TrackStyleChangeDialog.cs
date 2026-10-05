using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class TrackStyleChangeDialog : Window
{
    private readonly List<IDisposable> localizationBindings = [];

    internal TrackStyleChangeDialog(string trackName, string presetName, int subtitleCount)
    {
        localizationBindings.Add(this.Bind(TitleProperty, Localization.Observe("Workbench.TrackStyleChangeTitle").ToBinding()));
        Width = 470;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right
        };
        foreach (var (key, decision) in new[]
        {
            ("Cancel", TrackStyleUpdateDecision.CANCEL),
            ("No", TrackStyleUpdateDecision.DEFAULT_ONLY),
            ("Yes", TrackStyleUpdateDecision.UPDATE_EXISTING)
        })
        {
            var button = new Button
            {
                Name = key + "Button", Padding = new Thickness(14, 7),
                IsDefault = decision == TrackStyleUpdateDecision.DEFAULT_ONLY,
                IsCancel = decision == TrackStyleUpdateDecision.CANCEL
            };
            localizationBindings.Add(button.Bind(ContentControl.ContentProperty, Localization.Observe("Workbench." + key).ToBinding()));
            button.Click += (_, _) => Close(decision);
            buttons.Children.Add(button);
            if (decision == TrackStyleUpdateDecision.DEFAULT_ONLY)
            {
                Opened += (_, _) => button.Focus();
            }
        }

        var message = new TextBlock { TextWrapping = TextWrapping.Wrap };
        localizationBindings.Add(message.Bind(TextBlock.TextProperty, ObserveMessage(trackName, presetName, subtitleCount).ToBinding()));
        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 20,
            Children =
            {
                message,
                buttons
            }
        };
        Closed += OnClosed;
    }

    private static IObservable<string> ObserveMessage(string trackName, string presetName, int subtitleCount)
    {
        return Localization.Observe(() =>
            Localization.Format("Workbench.TrackStyleChangeText", trackName, presetName, subtitleCount));
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
