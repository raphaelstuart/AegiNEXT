using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.Effects;

/// <summary>特效设置页只组合个人库列表和通用脚本编辑器。</summary>
public sealed partial class EffectSettingsView : UserControl
{
    /// <summary>隔离父级上下文，避免页面编译绑定读取设置窗口模型。</summary>
    public EffectSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
    }
}
