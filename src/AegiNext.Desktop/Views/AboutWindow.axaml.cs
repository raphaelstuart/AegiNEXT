using AegiNext.Desktop.Controls.Common;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Views;

/// <summary>呈现应用图标、发布版本和版权许可信息。</summary>
public sealed partial class AboutWindow : Window
{
    /// <summary>创建使用当前程序集元数据的关于窗口。</summary>
    public AboutWindow()
    {
        DataContext = new AboutViewModel();
        AvaloniaXamlLoader.Load(this);
    }

    internal WindowTitleBar TitleBar => this.FindControl<WindowTitleBar>("AboutTitleBar")!;

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
