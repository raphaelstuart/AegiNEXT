using Avalonia;
using Avalonia.Controls;
using Dock.Avalonia.Controls;
using Dock.Model.Controls;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchFloatingHostWindow : HostWindow
{
    private readonly Action<WorkbenchFloatingHostWindow> closing;
    private readonly Action changed;

    internal WorkbenchFloatingHostWindow(WorkbenchDockFactory factory,
        Action<WorkbenchFloatingHostWindow> closing, Action changed)
    {
        this.closing = closing;
        this.changed = changed;
        MinWidth = 260;
        MinHeight = 180;
        DockHost = new()
        {
            Factory = factory, InitializeFactory = false, InitializeLayout = false
        };
        WorkbenchDockTemplateCatalog.Install(DockHost);
        Content = DockHost;
        PositionChanged += (_, _) => changed();
        SizeChanged += (_, _) => changed();
    }

    protected override Type StyleKeyOverride => typeof(Window);

    internal DockControl DockHost { get; }
    internal bool IsApplyingLayout { get; set; }

    /// <summary>将 Dock 提供的根上下文投影到空间宿主。</summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DataContextProperty && DockHost is not null)
        {
            DockHost.Layout = change.NewValue as IRootDock;
        }
    }

    /// <summary>浮窗关闭时先隐藏其中的面板，再脱离宿主而不释放工作台会话。</summary>
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (!IsApplyingLayout)
        {
            closing(this);
        }
        IsTracked = false;
        DockHost.Layout = null;
        base.OnClosing(e);
    }

    /// <summary>原生窗口关闭后安排保存最终空间状态。</summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        changed();
    }
}
