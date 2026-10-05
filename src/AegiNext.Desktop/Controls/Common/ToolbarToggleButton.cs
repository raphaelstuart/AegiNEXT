using Avalonia.Controls.Primitives;

namespace AegiNext.Desktop.Controls.Common;

/// <summary>使用工作台共享方形主题的工具栏开关，内容、命令和状态由使用方提供。</summary>
public sealed class ToolbarToggleButton : ToggleButton
{
    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(ToolbarToggleButton);
}
