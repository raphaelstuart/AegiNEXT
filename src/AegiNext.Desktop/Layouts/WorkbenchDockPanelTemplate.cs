using Avalonia.Controls;
using Avalonia.Controls.Templates;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchDockPanelTemplate : IDataTemplate
{
    /// <inheritdoc />
    public bool Match(object? data)
    {
        return data is WorkbenchDockPanel;
    }

    /// <summary>返回组合根持有的固定 View，使重新停靠保留业务状态和控件草稿。</summary>
    public Control? Build(object? data)
    {
        return data is WorkbenchDockPanel panel ? panel.View : null;
    }
}
