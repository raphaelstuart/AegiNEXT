using Avalonia.Controls;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>使用工作台共享紧凑分类主题的可折叠属性区域，展开状态由消费者持有。</summary>
public sealed class PropertySection : Expander
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(PropertySection);
}
