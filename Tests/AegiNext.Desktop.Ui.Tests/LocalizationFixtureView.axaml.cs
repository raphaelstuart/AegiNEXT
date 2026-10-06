using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>Loads compiled markup that combines business and localization bindings.</summary>
public sealed partial class LocalizationFixtureView : UserControl
{
    /// <summary>Loads the localization regression fixture.</summary>
    public LocalizationFixtureView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
