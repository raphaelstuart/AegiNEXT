using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Settings.ColorTags;

/// <summary>个人颜色标签的列表和名称、RGB 草稿输入。</summary>
public sealed partial class SubtitleColorTagsSettingsView : UserControl
{
    /// <summary>隔离设置页上下文后加载局部编译绑定。</summary>
    public SubtitleColorTagsSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
    }
}
