using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Media;

/// <summary>媒体设置页，只呈现偏好并发出语义切换请求。</summary>
public sealed partial class MediaSettingsView : UserControl
{
    /// <summary>加载绑定前隔离宿主页面上下文。</summary>
    public MediaSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
    }
}
