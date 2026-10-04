using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Appearance;

/// <summary>外观页面的编译绑定视图。</summary>
public sealed partial class AppearanceSettingsView : UserControl
{
    /// <summary>先隔离父级上下文，再加载外观页面的编译绑定。</summary>
    public AppearanceSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
    }
}
