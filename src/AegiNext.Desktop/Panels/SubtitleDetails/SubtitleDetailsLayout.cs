using Avalonia.Controls;

namespace AegiNext.Desktop.Panels.SubtitleDetails;

internal sealed class SubtitleDetailsLayout : Grid
{
    internal SubtitleDetailsLayout(Control toolbar, Control properties, Control editor, Control feedback)
    {
        Margin = new(12, 8);
        RowDefinitions = new("Auto,Auto,Auto,Auto");
        RowSpacing = 4;
        Children.Add(toolbar);
        Grid.SetRow(properties, 1);
        Children.Add(properties);
        Grid.SetRow(editor, 2);
        Children.Add(editor);
        Grid.SetRow(feedback, 3);
        Children.Add(feedback);
    }
}
