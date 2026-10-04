using Avalonia.Controls;
using Avalonia.Input;

namespace AegiNext.Desktop.Controls;

internal sealed class AnchorPresetButton : Button
{
    internal KeyModifiers SelectionModifiers { get; private set; }

    protected override Type StyleKeyOverride => typeof(Button);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        SelectionModifiers = e.KeyModifiers;
        base.OnPointerPressed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            SelectionModifiers = e.KeyModifiers;
        }
        base.OnKeyDown(e);
    }
}
