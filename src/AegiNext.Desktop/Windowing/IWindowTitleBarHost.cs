using AegiNext.Desktop.Controls.Common;

namespace AegiNext.Desktop.Windowing;

/// <summary>向窗口注册器交接已有标题栏及独立窗口装饰的所有权。</summary>
internal interface IWindowTitleBarHost
{
    WindowTitleBar TitleBar { get; }
    void ReleaseStandaloneChrome();
}
