using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Appearance;

/// <summary>外观页面的编译绑定视图。</summary>
public sealed partial class AppearanceSettingsView : UserControl
{
    /// <summary>构造本地控件，不读取业务状态。</summary>
    public AppearanceSettingsView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
