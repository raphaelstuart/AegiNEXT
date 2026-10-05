using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace AegiNext.Desktop.Views;

internal sealed class TrackStyleChangeDialog : Window
{
    internal TrackStyleChangeDialog(string trackName, string presetName, int subtitleCount)
    {
        Title = WorkbenchText.Get("TrackStyleChangeTitle");
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
                Name = key + "Button", Content = WorkbenchText.Get(key), Padding = new Thickness(14, 7),
                IsDefault = decision == TrackStyleUpdateDecision.DEFAULT_ONLY,
                IsCancel = decision == TrackStyleUpdateDecision.CANCEL
            };
            button.Click += (_, _) => Close(decision);
            buttons.Children.Add(button);
            if (decision == TrackStyleUpdateDecision.DEFAULT_ONLY)
            {
                Opened += (_, _) => button.Focus();
            }
        }

        Content = new StackPanel
        {
            Margin = new Thickness(20), Spacing = 20,
            Children =
            {
                new TextBlock
                {
                    Text = WorkbenchText.Format("TrackStyleChangeText", trackName, presetName, subtitleCount),
                    TextWrapping = TextWrapping.Wrap
                },
                buttons
            }
        };
    }
}
