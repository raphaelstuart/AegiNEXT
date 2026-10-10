using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectPropertyRowView : UserControl
{
    private readonly AnimationPropertyRowControl propertyRow;

    /// <summary>Creates a panel adapter for a shared animation property row.</summary>
    public EffectPropertyRowView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        propertyRow = this.FindControl<AnimationPropertyRowControl>("PropertyRow")!;
    }

    internal bool OwnsField(string? field)
    {
        return propertyRow.OwnsField(field);
    }

    internal bool FocusField(string? field)
    {
        return propertyRow.FocusField(field);
    }

    internal bool RestoreInput(Control source)
    {
        if (DataContext is not AnimationPropertyRowViewModel row || propertyRow.GetInputField(source) is not { } field)
        {
            return false;
        }
        row.Restore(field);
        return true;
    }
}
