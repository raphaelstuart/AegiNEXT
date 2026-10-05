using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Colors;

/// <summary>独立配色页，只负责输入与编译绑定。</summary>
public sealed partial class ColorsSettingsView : UserControl
{
    /// <summary>加载编译绑定前隔离父级设置窗口上下文。</summary>
    public ColorsSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
    }
}
