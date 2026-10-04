using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Dock.Avalonia.Controls;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchToolChromeControl : ToolChromeControl
{
    protected override Type StyleKeyOverride => typeof(ToolChromeControl);

    /// <inheritdoc />
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        foreach (var name in new[] { "PART_MenuButton", "PART_PinButton", "PART_MaximizeRestoreButton", "PART_CloseButton" })
        {
            if (e.NameScope.Find<Button>(name) is { } button)
            {
                button.Classes.Add("dock-chrome");
            }
        }
    }
}
