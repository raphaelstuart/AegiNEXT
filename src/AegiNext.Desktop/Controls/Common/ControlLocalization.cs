using AegiNext.Desktop.Localization;
using AegiNext.Desktop.Styling;
using Avalonia.Controls;
using Avalonia.LogicalTree;

namespace AegiNext.Desktop.Controls.Common;

internal static class ControlLocalization
{
    internal static void Apply(Control root)
    {
        foreach (var control in root.GetLogicalDescendants().OfType<Control>())
        {
            if (control.Tag is not string key)
            {
                continue;
            }

            var text = WorkbenchText.Get(key);
            switch (control)
            {
                case TextBlock block:
                    block.Text = text;
                    break;
                case Button button:
                    button.Content = WorkbenchIcon.Content(text, key);
                    break;
                case ContentControl content:
                    content.Content = text;
                    break;
            }
        }
    }
}
