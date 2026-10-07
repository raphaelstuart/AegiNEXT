using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Transfer;

/// <summary>呈现个人设置导出、导入预览与下次启动恢复操作。</summary>
public sealed partial class UserSettingsTransferView : UserControl
{
    /// <summary>加载独立页面并隔离宿主初始化时的上下文。</summary>
    public UserSettingsTransferView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
    }
}
