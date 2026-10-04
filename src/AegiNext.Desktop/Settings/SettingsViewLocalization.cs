using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Styling;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace AegiNext.Desktop.Settings;

internal static class SettingsViewLocalization
{
    internal static void Apply(Control root)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>())
        {
            if (control is AnchorPresetPicker presets)
            {
                presets.RefreshLanguage();
            }
            if (control.Tag is not string key)
            {
                continue;
            }

            var text = SettingsText.Get(key);
            switch (control)
            {
                case TextBlock block:
                    block.Text = text;
                    break;
                case Expander expander:
                    expander.Header = text;
                    break;
                case Button button:
                    SetButtonContent(button, text, key);
                    break;
                case ContentControl content:
                    content.Content = text;
                    break;
            }
        }
    }

    internal static void SetButtonContent(Button button, string text, string key)
    {
        var icon = key switch
        {
            "Record" or "Recording" => "Keyframe",
            "Clear" => "Close",
            "Reset" => "Undo",
            "Duplicate" => "File",
            "Capture" => "Subtitles",
            "Apply" => "Style",
            _ => key
        };
        button.Content = WorkbenchIcon.Content(text, icon);
    }
}
