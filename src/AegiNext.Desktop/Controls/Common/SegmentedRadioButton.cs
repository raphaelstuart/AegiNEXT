using Avalonia.Controls;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>使用工作台紧凑分段主题的单选按钮；分组、内容与选中状态由使用方提供。</summary>
public sealed class SegmentedRadioButton : RadioButton
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(SegmentedRadioButton);
}
